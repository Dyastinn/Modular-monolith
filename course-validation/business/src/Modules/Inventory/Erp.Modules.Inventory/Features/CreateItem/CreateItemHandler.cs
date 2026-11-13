using Erp.Modules.Inventory.Domain;
using Erp.Modules.Inventory.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Inventory.Features;

internal sealed record CreateItemCommand(string Sku, string Name, string Unit, bool AllowsFractions);

internal sealed class CreateItemHandler(InventoryDbContext db)
{
    public async Task<long> HandleAsync(CreateItemCommand cmd, CancellationToken ct)
    {
        var sku = cmd.Sku.Trim().ToUpperInvariant();
        if (await db.Items.AnyAsync(i => i.Sku == sku, ct))
            throw new DomainException(InventoryErrors.SkuTaken, $"An item with SKU {sku} already exists.");

        var id = new ItemId(await db.NextItemIdAsync(ct));
        db.Items.Add(Item.Create(id, sku, cmd.Name, cmd.Unit, cmd.AllowsFractions));
        await db.SaveChangesAsync(ct);
        return id.Value;
    }
}
