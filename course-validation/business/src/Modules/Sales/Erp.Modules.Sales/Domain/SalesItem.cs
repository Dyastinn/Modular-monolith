using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

// Sales' copy of a catalog item, plus what only Sales decides: the price, the category, and whether it's for sale.
// The catalog half (SKU, name, unit, active) is kept in step with Inventory by ItemCatalogChanged.
internal sealed class SalesItem : Entity<ItemId>
{
    public string Sku { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string Unit { get; private set; } = default!;
    public bool AllowsFractions { get; private set; }
    public bool IsActive { get; private set; }
    public Money UnitPrice { get; private set; }
    public CategoryId? CategoryId { get; private set; }
    public ItemCategory? Category { get; private set; }
    public bool IsForSale { get; private set; }
    public TaxType TaxType { get; private set; }
    public bool IsSpecialDiscountEligible { get; private set; }   // e.g. medicines, for senior citizens and PWDs

    private SalesItem() { }

    // A new catalog item arrives unpriced and not for sale.
    public static SalesItem FromCatalog(ItemId id, string sku, string name, string unit, bool allowsFractions, bool isActive)
    {
        var item = new SalesItem { Id = id };
        item.ApplyCatalog(sku, name, unit, allowsFractions, isActive);
        return item;
    }

    public void ApplyCatalog(string sku, string name, string unit, bool allowsFractions, bool isActive)
    {
        Sku = sku; Name = name; Unit = unit; AllowsFractions = allowsFractions; IsActive = isActive;
    }

    public void SetPricing(Money unitPrice, ItemCategory category, bool isForSale, TaxType taxType, bool specialDiscountEligible)
    {
        if (!Enum.IsDefined(taxType)) throw new DomainException($"'{taxType}' is not a tax type.");
        if (isForSale && unitPrice == Money.Zero) throw new DomainException($"Set a price for {Sku} before putting it on sale.");
        if (isForSale && !IsActive) throw new DomainException($"{Sku} is inactive in the catalog and can't be sold.");
        UnitPrice = unitPrice;
        CategoryId = category.Id;
        Category = category;
        IsForSale = isForSale;
        TaxType = taxType;
        IsSpecialDiscountEligible = specialDiscountEligible;
    }

    public ItemCategory EnsureSellable()
    {
        if (!IsActive || !IsForSale || Category is null)
            throw new DomainException($"Item {Sku} is not for sale.");
        return Category;
    }

    public void EnsureQuantityFits(Quantity quantity)
    {
        if (!AllowsFractions && !quantity.IsWhole)
            throw new DomainException($"Item {Sku} is sold in whole {Unit}; {quantity} is not allowed.");
    }
}
