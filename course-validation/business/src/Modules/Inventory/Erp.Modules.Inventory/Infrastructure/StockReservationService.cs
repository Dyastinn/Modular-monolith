using System.Data.Common;
using Erp.Modules.Inventory.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Inventory.Infrastructure;

internal sealed class StockReservationService(InventoryDbContext db) : IStockReservation
{
    // Check and write in ONE statement, so two orders racing for the last units can't both win.
    public Task<bool> TryReserveAsync(long itemId, int quantity, DbTransaction tx, CancellationToken ct) =>
        InTransaction(tx, async () =>
        {
            var rows = await db.StockItems
                .Where(s => s.ItemId == itemId && s.OnHand - s.Reserved >= quantity)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Reserved, x => x.Reserved + quantity), ct);
            return rows == 1;
        }, ct);

    public Task ReleaseAsync(long itemId, int quantity, DbTransaction tx, CancellationToken ct) =>
        InTransaction(tx, async () =>
        {
            await db.StockItems
                .Where(s => s.ItemId == itemId)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    x => x.Reserved, x => x.Reserved - quantity < 0 ? 0 : x.Reserved - quantity), ct);
            return true;
        }, ct);

    // Join the caller's transaction for one operation, then let go: the caller owns the commit.
    private async Task<bool> InTransaction(DbTransaction tx, Func<Task<bool>> work, CancellationToken ct)
    {
        await db.Database.UseTransactionAsync(tx, ct);
        try { return await work(); }
        finally { await db.Database.UseTransactionAsync(null, ct); }
    }
}
