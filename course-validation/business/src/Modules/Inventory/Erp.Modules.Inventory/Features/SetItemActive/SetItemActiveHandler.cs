using Erp.Modules.Inventory.Domain;
using Erp.Modules.Inventory.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Inventory.Features;

internal sealed record SetItemActiveCommand(long ItemId, bool Active);

internal sealed class SetItemActiveHandler(InventoryDbContext db)
{
    public async Task<bool> HandleAsync(SetItemActiveCommand cmd, CancellationToken ct)
    {
        var item = await db.Items.FindAsync([new ItemId(cmd.ItemId)], ct);
        if (item is null) return false;
        item.SetActive(cmd.Active);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
