using System.Data.Common;
using Erp.Modules.Inventory.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Inventory.Infrastructure;

// The safety lives in ONE statement: "add to Reserved only WHERE enough is still available". Two orders racing
// for the last 5 units can't both win, because SQL Server checks and writes in the same step.
internal sealed class StockReservationService(InventoryDbContext db) : IStockReservation
{
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
            // Never below zero, even if a caller over-releases (the CHECK constraint backstops this too).
            await db.StockItems
                .Where(s => s.ItemId == itemId)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    x => x.Reserved, x => x.Reserved - quantity < 0 ? 0 : x.Reserved - quantity), ct);
            return true;
        }, ct);

    // Join the caller's transaction for this one operation, then let go of it — the caller owns the commit.
    private async Task<bool> InTransaction(DbTransaction tx, Func<Task<bool>> work, CancellationToken ct)
    {
        await db.Database.UseTransactionAsync(tx, ct);
        try { return await work(); }
        finally { await db.Database.UseTransactionAsync(null, ct); }
    }
}
