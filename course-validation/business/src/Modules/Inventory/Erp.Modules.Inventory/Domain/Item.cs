using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Inventory.Domain;

internal readonly record struct ItemId(long Value);
internal readonly record struct UserId(long Value);

// The item catalog and its stock. Other modules keep their own copy of what they need (Sales: name and unit),
// kept in step by ItemCatalogChanged.
internal sealed class Item : AggregateRoot<ItemId>
{
    public string Sku { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string Unit { get; private set; } = default!;
    public bool AllowsFractions { get; private set; }
    public bool IsActive { get; private set; }
    public decimal OnHand { get; private set; }
    public decimal Reserved { get; private set; }   // promised to submitted sales orders
    public decimal Available => OnHand - Reserved;

    private Item() { }

    public static Item Create(ItemId id, string sku, string name, string unit, bool allowsFractions)
    {
        var item = new Item { Id = id, Sku = Required(sku, "SKU").ToUpperInvariant(), IsActive = true };
        item.Describe(name, unit, allowsFractions);
        return item;
    }

    public void Describe(string name, string unit, bool allowsFractions)
    {
        Name = Required(name, "Name");
        Unit = Required(unit, "Unit").ToLowerInvariant();
        AllowsFractions = allowsFractions;
        RaiseChanged();
    }

    public void SetActive(bool active)
    {
        if (!active && Reserved > 0)
            throw new DomainException(InventoryErrors.ItemHasReservations, $"Item {Sku} has {Reserved:0.###} {Unit} reserved for orders and can't be deactivated.");
        IsActive = active;
        RaiseChanged();
    }

    public void EnsureQuantityFits(decimal quantity)
    {
        if (!AllowsFractions && decimal.Truncate(quantity) != quantity)
            throw new DomainException(InventoryErrors.WholeUnitsOnly, $"Item {Sku} is counted in whole {Unit}; {quantity:0.###} is not allowed.");
    }

    // A count correction: found, lost, damaged. Never below what's already promised to orders.
    public StockMovement Adjust(decimal delta, string reason, UserId by, DateTime nowUtc)
    {
        // Every check first: a refused adjustment must leave the item exactly as it was.
        if (delta == 0) throw new DomainException(InventoryErrors.AdjustmentMustChangeQuantity, "An adjustment must change the quantity.");
        var why = Required(reason, "Reason");
        EnsureQuantityFits(Math.Abs(delta));
        if (OnHand + delta < Reserved)
            throw new DomainException(InventoryErrors.AdjustmentBelowReserved,
                $"Adjusting {Sku} by {delta:0.###} would leave {OnHand + delta:0.###} {Unit} on hand, " +
                $"less than the {Reserved:0.###} reserved for orders.");
        OnHand += delta;
        return StockMovement.Adjustment(Id, delta, why, by, nowUtc);
    }

    private void RaiseChanged() =>
        Raise(new ItemChanged(Id, Sku, Name, Unit, AllowsFractions, IsActive));

    private static string Required(string value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new DomainException(InventoryErrors.FieldRequired, $"{field} is required.") : value.Trim();
}

// The catalog half of an item changed (created, renamed, activated...). Published to other modules as ItemCatalogChanged.
internal sealed record ItemChanged(ItemId ItemId, string Sku, string Name, string Unit, bool AllowsFractions, bool IsActive) : IDomainEvent;
