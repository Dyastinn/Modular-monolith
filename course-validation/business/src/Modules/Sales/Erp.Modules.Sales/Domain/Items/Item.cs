namespace Erp.Modules.Sales.Domain;

// Something Sales can put on an order: its price and the category rules that limit who may order how much.
internal sealed class Item
{
    public long Id { get; private set; }
    public string Sku { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public long CategoryId { get; private set; }
    public ItemCategory Category { get; private set; } = default!;
    public Money UnitPrice { get; private set; }
    public bool IsActive { get; private set; }

    private Item() { }   // for EF
}
