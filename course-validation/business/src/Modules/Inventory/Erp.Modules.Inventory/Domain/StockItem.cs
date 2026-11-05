namespace Erp.Modules.Inventory.Domain;

// ItemId refers to an item in another module: by id only, never a cross-schema FK.
internal sealed class StockItem
{
    public long ItemId { get; private set; }
    public int OnHand { get; private set; }
    public int Reserved { get; private set; }   // promised to submitted orders
    public int Available => OnHand - Reserved;

    private StockItem() { }
}
