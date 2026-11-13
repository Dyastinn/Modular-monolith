using Erp.Modules.Inventory.Domain;
using Erp.Modules.Inventory.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Inventory.Features;

internal sealed record AdjustStockCommand(long ItemId, decimal Quantity, string Reason);

internal sealed class AdjustStockHandler(InventoryDbContext db, ICurrentUser user, TimeProvider time)
{
    public async Task<bool> HandleAsync(AdjustStockCommand cmd, CancellationToken ct)
    {
        var item = await db.Items.FindAsync([new ItemId(cmd.ItemId)], ct);
        if (item is null) return false;
        db.StockMovements.Add(item.Adjust(cmd.Quantity, cmd.Reason, new UserId(user.UserId), time.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);   // xmin: a reservation that landed meanwhile makes this a 409
        return true;
    }
}
