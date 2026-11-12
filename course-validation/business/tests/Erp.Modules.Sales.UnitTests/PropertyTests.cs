using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;
using FsCheck;
using FsCheck.Xunit;

namespace Erp.Modules.Sales.UnitTests;

// A [Fact] checks the examples we thought of; a [Property] checks a rule against a hundred inputs FsCheck makes up.
public class MoneyProperties
{
    [Property]
    public bool Money_always_holds_whole_centavos(NonNegativeInt pesos, NonNegativeInt thousandths)
    {
        var amount = pesos.Get + thousandths.Get % 1000 / 1000m;
        var money = new Money(amount);
        return money.Amount * 100 == decimal.Truncate(money.Amount * 100) && Math.Abs(money.Amount - amount) <= 0.005m;
    }

    [Property]
    public bool Adding_amounts_never_drifts_a_centavo(NonNegativeInt[] centavos)
    {
        var sum = centavos.Aggregate(Money.Zero, (total, c) => total + new Money(c.Get / 100m));
        return sum.Amount == centavos.Sum(c => (decimal)c.Get) / 100m;
    }

    [Property]
    public bool Subtracting_what_was_added_gives_back_the_start(NonNegativeInt start, NonNegativeInt added)
    {
        var a = new Money(start.Get / 100m);
        var b = new Money(added.Get / 100m);
        return a + b - b == a;
    }
}

public class OrderTotalProperties
{
    // The C# total and the SQL functions' SUM(ROUND(quantity * unit_price, 2)) must agree, or the list and the
    // detail page show different totals.
    [Property]
    public bool An_order_total_is_the_sum_of_its_lines_rounded_one_by_one(PositiveInt[] priceCentavos, PositiveInt[] gramsSeed)
    {
        var count = Math.Min(Math.Min(priceCentavos.Length, gramsSeed.Length), 5);
        if (count == 0) return true;

        var lines = Enumerable.Range(0, count).Select(i =>
        {
            var price = priceCentavos[i].Get % 10_000_000 / 100m + 0.01m;
            var quantity = gramsSeed[i].Get % 200_000 / 1000m + 0.001m;   // up to 200 kg, gram by gram
            return (Item: Make.Item(i + 1, price, fractions: true, unit: "kg"), Price: price, Quantity: quantity);
        }).ToList();

        var order = SalesOrder.Create("SO-1", new CustomerId(0), Make.Rep, Make.Now);
        order.SetLines(lines.Select(l => (l.Item, new Quantity(l.Quantity))).ToList(), Roles.SalesRep);

        return order.Total.Amount == lines.Sum(l => Math.Round(l.Price * l.Quantity, 2, MidpointRounding.AwayFromZero));
    }
}

public class SpecialDiscountProperties
{
    private static readonly TaxPolicy Vat12 = new(TaxRegime.Vat, 0.12m);

    private static List<(Money Gross, TaxType TaxType, bool Eligible)> Lines(PositiveInt[] centavos, bool[] eligible) =>
        centavos.Take(8).Select((c, i) => (
            new Money(c.Get % 10_000_000 / 100m),
            (TaxType)(i % 3),
            eligible.Length > 0 && eligible[i % eligible.Length])).ToList();

    private static SpecialDiscount Senior() =>
        SpecialDiscount.Grant("Senior citizen", "OSCA ID No.", 0.20m, vatExempt: true, "Lola Basyang", "OSCA-1", Vat12);

    [Property]
    public bool The_discount_never_exceeds_what_the_eligible_items_cost(PositiveInt[] centavos, bool[] eligible)
    {
        var lines = Lines(centavos, eligible);
        var amounts = Senior().Apply(lines);
        var eligibleGross = lines.Where(l => l.Eligible).Sum(l => l.Gross.Amount);
        return amounts.Total.Amount <= eligibleGross;
    }

    // The order and the invoice list their lines in their own order; the discount must not care.
    [Property]
    public bool The_order_of_the_lines_never_changes_the_discount(PositiveInt[] centavos, bool[] eligible)
    {
        var lines = Lines(centavos, eligible);
        var reversed = Enumerable.Reverse(lines).ToList();
        return Senior().Apply(lines) == Senior().Apply(reversed);
    }
}
