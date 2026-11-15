using AwesomeAssertions;
using Erp.Modules.Invoicing.Domain;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Invoicing.UnitTests;

// What a customer owed before go-live: a debt like any invoice for payments and aging, but never a sale.
public class OpeningBalanceTests
{
    private static readonly SellerDetails Seller = new("Demo Trading Corp.", null, "000-111-222-00000", "Pasig", "SALES INVOICE", null, null, null);
    private static readonly DateOnly Today = new(2026, 11, 15);
    private static readonly DateTime Now = new(2026, 11, 15, 1, 0, 0, DateTimeKind.Utc);

    private static Invoice Opening(decimal amount = 18_500, DateOnly? issued = null, DateOnly? due = null, string reference = "OLD-42") =>
        Invoice.OpeningBalance("OB-1", Seller, 7, "Old Debtor", null, null, reference, issued ?? Today.AddDays(-40),
            due ?? Today.AddDays(-10), new Money(amount), Today);

    [Fact]
    public void It_is_a_debt_with_no_sale_in_it()
    {
        var ob = Opening();

        ob.IsOpeningBalance.Should().BeTrue();
        ob.BalanceDue.Should().Be(new Money(18_500));
        (ob.VatableSales, ob.VatAmount, ob.VatExemptSales, ob.ZeroRatedSales).Should().Be((Money.Zero, Money.Zero, Money.Zero, Money.Zero));
        ob.DomainEvents.Should().ContainSingle().Which.Should().Be(new InvoiceOpeningBalanceRecorded("OB-1", 7, new Money(18_500)));
    }

    [Fact]
    public void Payments_settle_it_like_an_invoice()
    {
        var ob = Opening(1_000);
        ob.RecordPayment(new PaymentId(1), new Money(1_000), Money.Zero, Today, PaymentMethod.Cash, null, new UserId(3), Today, Now);
        ob.Status.Should().Be(InvoiceStatus.Paid);
    }

    [Fact]
    public void Its_rules_each_have_their_code()
    {
        FluentActions.Invoking(() => Opening(0)).Should().Throw<DomainException>().Which.Error.Should().Be(InvoicingErrors.OpeningBalanceNotPositive);
        FluentActions.Invoking(() => Opening(issued: Today.AddDays(1), due: Today.AddDays(30)))
            .Should().Throw<DomainException>().Which.Error.Should().Be(InvoicingErrors.OpeningBalanceInFuture);
        FluentActions.Invoking(() => Opening(issued: Today.AddDays(-5), due: Today.AddDays(-6)))
            .Should().Throw<DomainException>().Which.Error.Should().Be(InvoicingErrors.DueBeforeIssue);
        FluentActions.Invoking(() => Opening(reference: " ")).Should().Throw<DomainException>()
            .Which.Error.Should().Be(InvoicingErrors.OpeningBalanceReferenceRequired);
    }

    [Fact]
    public void Voiding_tells_sales_and_it_is_never_reissued()
    {
        var ob = Opening();
        ob.ClearEvents();

        ob.Void("Paid before go-live", new UserId(3), Now);

        ob.DomainEvents.Should().ContainSingle().Which.Should().Be(new InvoiceOpeningBalanceVoided("OB-1", 7, new Money(18_500)));
        ob.Invoking(o => o.EnsureCanBeReissued()).Should().Throw<DomainException>()
            .Which.Error.Should().Be(InvoicingErrors.OpeningBalanceNotReissued);
    }
}
