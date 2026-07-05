namespace Erp.Modules.Sales.Domain;

// NOTE: SalesOrderLine is listed in "what to produce" but no code is given in Phase 3.
// Inferred from usage: constructed with (product, qty, unitPrice); exposes LineTotal (Money).
public sealed class SalesOrderLine
{
    // Named ProductId to match the plan's Phase 5 usage `line.ProductId.Value` (SalesOrderLine is
    // never actually defined in the plan, so the reader must guess this name correctly in Phase 3).
    public ProductId ProductId { get; private set; }
    public Quantity Quantity { get; private set; }
    public Money UnitPrice { get; private set; }
    public Money LineTotal => UnitPrice * Quantity.Value;

    public SalesOrderLine(ProductId product, Quantity qty, Money unitPrice)
    {
        ProductId = product;
        Quantity = qty;
        UnitPrice = unitPrice;
    }
}
