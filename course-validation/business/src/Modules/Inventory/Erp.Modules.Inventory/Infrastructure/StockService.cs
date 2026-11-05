using System.Data.Common;
using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Inventory.Domain;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Inventory.Infrastructure;

// Inventory's service window: what other modules may ask of it.
internal sealed class StockService(InventoryDbContext db, IClock clock) : IStockReservation, IStockReceiving, IItemCatalog
{
    // Check and write in ONE statement, so two orders racing for the last units can't both win.
    public Task<bool> TryReserveAsync(long itemId, decimal quantity, DbTransaction tx, CancellationToken ct) =>
        InTransaction(tx, async () =>
        {
            var id = new ItemId(itemId);
            var rows = await db.Items
                .Where(i => i.Id == id && i.IsActive && i.OnHand - i.Reserved >= quantity)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Reserved, i => i.Reserved + quantity), ct);
            return rows == 1;
        }, ct);

    public Task ReleaseAsync(long itemId, decimal quantity, DbTransaction tx, CancellationToken ct) =>
        InTransaction(tx, async () =>
        {
            var id = new ItemId(itemId);
            await db.Items.Where(i => i.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    i => i.Reserved, i => i.Reserved - quantity < 0 ? 0 : i.Reserved - quantity), ct);
            return true;
        }, ct);

    public Task ReceiveAsync(long itemId, decimal quantity, string reference, long userId, DbTransaction tx, CancellationToken ct) =>
        InTransaction(tx, async () =>
        {
            var id = new ItemId(itemId);
            var rows = await db.Items.Where(i => i.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.OnHand, i => i.OnHand + quantity), ct);
            if (rows == 0) throw new NotFoundException($"Item {itemId} not found.");

            db.StockMovements.Add(StockMovement.Receipt(id, quantity, reference, new UserId(userId), clock.UtcNow));
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);

    public async Task<IReadOnlyList<CatalogItem>> GetAsync(IReadOnlyCollection<long> itemIds, CancellationToken ct)
    {
        var ids = itemIds.Select(i => new ItemId(i)).ToList();
        return await db.Items.AsNoTracking()
            .Where(i => ids.Contains(i.Id))
            .Select(i => new CatalogItem(i.Id.Value, i.Sku, i.Name, i.Unit, i.AllowsFractions, i.IsActive))
            .ToListAsync(ct);
    }

    // Join the caller's transaction for one operation, then let go: the caller owns the commit.
    private async Task<T> InTransaction<T>(DbTransaction tx, Func<Task<T>> work, CancellationToken ct)
    {
        await db.Database.UseTransactionAsync(tx, ct);
        try { return await work(); }
        finally { await db.Database.UseTransactionAsync(null, ct); }
    }
}
