using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Features;

internal sealed record SetItemPricingCommand(long ItemId, decimal UnitPrice, long CategoryId, bool IsForSale, TaxType TaxType,
    bool SpecialDiscountEligible);

internal sealed class SetItemPricingHandler(SalesDbContext db)
{
    public async Task<bool> HandleAsync(SetItemPricingCommand cmd, CancellationToken ct)
    {
        var item = await db.SalesItems.FindAsync([new ItemId(cmd.ItemId)], ct);
        if (item is null) return false;
        var category = await db.ItemCategories.FindAsync([new CategoryId(cmd.CategoryId)], ct)
            ?? throw new DomainException(SalesErrors.CategoryNotFound, $"Category {cmd.CategoryId} doesn't exist.");

        item.SetPricing(new Money(cmd.UnitPrice), category, cmd.IsForSale, cmd.TaxType, cmd.SpecialDiscountEligible);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
