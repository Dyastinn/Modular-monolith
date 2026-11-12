using Erp.Modules.Invoicing.Contracts;
using Erp.Modules.Invoicing.Domain;
using Erp.Shared.BuildingBlocks;
using FsCheck;
using FsCheck.Xunit;

namespace Erp.Modules.Invoicing.UnitTests;

// The BIR checks that the printed breakdown adds back to the amount due. FsCheck tries a hundred invoices for us.
public class TaxBreakdownProperties
{
    private static readonly TaxPolicy Vat12 = new(TaxRegime.Vat, 0.12m);
    private static readonly SellerDetails Seller = new("Demo Trading Corp.", null, "000-111-222-00000", "Pasig", "SALES INVOICE", null, null, null);

    private static Invoice Issue(PositiveInt[] centavos, PositiveInt[] thousandths, bool[] eligible, SpecialDiscount? discount)
    {
        var count = Math.Min(Math.Min(centavos.Length, thousandths.Length), 8);
        var lines = Enumerable.Range(0, count).Select(i => new InvoiceLine($"Item {i}", "kg",
            new Quantity(thousandths[i].Get % 100_000 / 1000m + 0.001m),
            new Money(centavos[i].Get % 10_000_000 / 100m + 0.01m),
            (TaxType)(i % 3),
            eligible.Length > 0 && eligible[i % eligible.Length])).ToList();

        return Invoice.Issue("SI-1", Seller, shipmentId: 1, salesOrderId: 1, "SO-1", customerId: 7, "Acme", null, "Makati",
            paymentTermsDays: 30, lines, new DateOnly(2026, 11, 5), Vat12, discount);
    }

    [Property]
    public bool The_breakdown_always_adds_back_to_the_total(PositiveInt[] centavos, PositiveInt[] thousandths)
    {
        if (centavos.Length == 0 || thousandths.Length == 0) return true;
        var invoice = Issue(centavos, thousandths, [], null);
        return invoice.VatableSales + invoice.VatAmount + invoice.ZeroRatedSales + invoice.VatExemptSales == invoice.Total;
    }

    // With a senior's discount the breakdown shows the sales before the 20%, so it adds back to total + discount.
    [Property]
    public bool With_a_special_discount_the_breakdown_adds_back_to_the_total_plus_the_discount(
        PositiveInt[] centavos, PositiveInt[] thousandths, bool[] eligible)
    {
        if (centavos.Length == 0 || thousandths.Length == 0 || !eligible.Contains(true)) return true;
        var senior = SpecialDiscount.Grant("Senior citizen", "OSCA ID No.", 0.20m, vatExempt: true, "Lola Basyang", "OSCA-1", Vat12);
        var invoice = Issue(centavos, thousandths, eligible, senior);
        return invoice.VatableSales + invoice.VatAmount + invoice.ZeroRatedSales + invoice.VatExemptSales
               == invoice.Total + invoice.SpecialDiscountAmount;
    }
}
