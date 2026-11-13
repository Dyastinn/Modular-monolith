using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Infrastructure;

// Keeps Sales' copy of the catalog in step with Inventory. It writes the full state each time, so a repeated or
// late delivery leaves the same result: idempotent.
internal sealed class ItemCatalogChangedHandler(SalesDbContext db) : IIntegrationEventHandler<ItemCatalogChanged>
{
    public async Task HandleAsync(ItemCatalogChanged e, CancellationToken ct)
    {
        var item = await db.SalesItems.FindAsync([new ItemId(e.ItemId)], ct);
        if (item is null)
            db.SalesItems.Add(SalesItem.FromCatalog(new ItemId(e.ItemId), e.Sku, e.Name, e.Unit, e.AllowsFractions, e.IsActive));
        else
            item.ApplyCatalog(e.Sku, e.Name, e.Unit, e.AllowsFractions, e.IsActive);
        await db.SaveChangesAsync(ct);
    }
}
