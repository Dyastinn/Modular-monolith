namespace Erp.Modules.Inventory.Domain;

// How much of one item the warehouse holds, and how much of that is already promised to orders.
// ItemId points at an item in another module's catalog — by id only, never an FK across schemas.
internal sealed class StockItem
{
    public long ItemId { get; private set; }
    public int OnHand { get; private set; }     // physically in the warehouse
    public int Reserved { get; private set; }   // promised to submitted orders, not yet shipped
    public int Available => OnHand - Reserved;

    private StockItem() { }   // for EF
}
