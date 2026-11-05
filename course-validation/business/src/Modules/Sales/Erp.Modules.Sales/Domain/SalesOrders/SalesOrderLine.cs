namespace Erp.Modules.Sales.Domain;

internal sealed class SalesOrderLine
{
    public ItemId ItemId { get; private set; }
    public Quantity Quantity { get; private set; }
    public Money UnitPrice { get; private set; }
    public Money LineTotal => UnitPrice * Quantity.Value;

    private SalesOrderLine() { }   // for EF

    public SalesOrderLine(ItemId itemId, Quantity qty, Money unitPrice)
    { ItemId = itemId; Quantity = qty; UnitPrice = unitPrice; }

    public void IncreaseQuantity(Quantity extra) => Quantity = new Quantity(Quantity.Value + extra.Value);
}
