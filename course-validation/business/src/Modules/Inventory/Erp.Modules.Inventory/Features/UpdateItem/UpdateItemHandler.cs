using Erp.Modules.Inventory.Domain;
using Erp.Modules.Inventory.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Inventory.Features;

internal sealed record UpdateItemCommand(long ItemId, string Name, string Unit, bool AllowsFractions);

internal sealed class UpdateItemHandler(InventoryDbContext db)
{
    public async Task<bool> HandleAsync(UpdateItemCommand cmd, CancellationToken ct)
    {
        var item = await db.Items.FindAsync([new ItemId(cmd.ItemId)], ct);
        if (item is null) return false;
        item.Describe(cmd.Name, cmd.Unit, cmd.AllowsFractions);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
