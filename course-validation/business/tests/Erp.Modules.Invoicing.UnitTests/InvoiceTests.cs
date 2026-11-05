using AwesomeAssertions;
using Erp.Modules.Invoicing.Contracts;
using Erp.Modules.Invoicing.Domain;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Invoicing.UnitTests;

public class InvoiceTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Issued = new(2026, 11, 5);
    private static readonly UserId Accountant = new(6);

    private static Invoice Invoice() => Domain.Invoice.Issue("SI-1", shipmentId: 1, salesOrderId: 1, "SO-1", customerId: 7,
        "Acme", "123", "Makati", paymentTermsDays: 30,
        [new InvoiceLine("Widget", "pc", new Quantity(2), new Money(25)), new InvoiceLine("Wire", "kg", new Quantity(1.5m), new Money(620.50m))],
        Issued);

    private static void Pay(Invoice invoice, long id, decimal amount, DateOnly? on = null) =>
        invoice.RecordPayment(new PaymentId(id), new Money(amount), on ?? Issued, PaymentMethod.Cash, null, Accountant, Issued, Now);

    [Fact]
    public void An_invoice_totals_its_lines_and_is_due_after_the_terms()
    {
        var invoice = Invoice();
        invoice.Total.Should().Be(new Money(980.75m));
        invoice.DueDate.Should().Be(new DateOnly(2026, 12, 5));
        invoice.Status.Should().Be(InvoiceStatus.Issued);
    }

    [Fact]
    public void Payments_move_it_to_partly_paid_then_paid_and_tell_Sales()
    {
        var invoice = Invoice();

        Pay(invoice, 1, 500);
        invoice.Status.Should().Be(InvoiceStatus.PartiallyPaid);
        Pay(invoice, 2, 480.75m);
        invoice.Status.Should().Be(InvoiceStatus.Paid);

        invoice.DomainEvents.Should().Equal(new PaymentRecorded(1, 7, 500), new PaymentRecorded(2, 7, 480.75m));
    }

    [Fact]
    public void A_payment_can_never_exceed_the_balance_or_be_dated_oddly()
    {
        var invoice = Invoice();
        invoice.Invoking(i => Pay(i, 1, 1000)).Should().Throw<DomainException>().WithMessage("*left to pay*");
        invoice.Invoking(i => Pay(i, 1, 10, Issued.AddDays(-1))).Should().Throw<DomainException>().WithMessage("*before the invoice*");
        invoice.Invoking(i => Pay(i, 1, 10, Issued.AddDays(1))).Should().Throw<DomainException>().WithMessage("*future*");
    }

    [Fact]
    public void Voiding_a_payment_reopens_the_balance_and_tells_Sales()
    {
        var invoice = Invoice();
        Pay(invoice, 1, 980.75m);

        invoice.VoidPayment(new PaymentId(1), "Check bounced", Now);

        invoice.Status.Should().Be(InvoiceStatus.Issued);
        invoice.BalanceDue.Should().Be(new Money(980.75m));
        invoice.DomainEvents.Last().Should().Be(new PaymentVoided(1, 7, 980.75m));
        invoice.Invoking(i => i.VoidPayment(new PaymentId(1), "again", Now)).Should().Throw<DomainException>();
    }

    [Fact]
    public void An_invoice_with_payments_cannot_be_voided()
    {
        var invoice = Invoice();
        Pay(invoice, 1, 100);
        invoice.Invoking(i => i.Void("Wrong price", Accountant, Now)).Should().Throw<DomainException>().WithMessage("*payments*");
    }

    [Fact]
    public void Only_a_void_invoice_can_be_reissued()
    {
        var invoice = Invoice();
        invoice.Invoking(i => i.EnsureCanBeReissued()).Should().Throw<DomainException>();
        invoice.Void("Wrong price", Accountant, Now);
        invoice.Invoking(i => i.EnsureCanBeReissued()).Should().NotThrow();
        invoice.Invoking(i => Pay(i, 1, 10)).Should().Throw<DomainException>().WithMessage("*void*");
    }

    [Fact]
    public void The_VAT_split_adds_back_to_the_total()
    {
        var (vatable, vat) = VatBreakdown.Of(new Money(980.75m));
        vatable.Should().Be(new Money(875.67m));
        vat.Should().Be(new Money(105.08m));
        (vatable + vat).Should().Be(new Money(980.75m));
    }
}
