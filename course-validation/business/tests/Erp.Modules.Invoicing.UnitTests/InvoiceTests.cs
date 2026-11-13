using AwesomeAssertions;
using Erp.Modules.Invoicing.Domain;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Invoicing.UnitTests;

public class InvoiceTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Issued = new(2026, 11, 5);
    private static readonly UserId Accountant = new(6);

    private static readonly TaxPolicy Vat12 = new(TaxRegime.Vat, 0.12m);
    private static readonly SellerDetails Seller = new("Demo Trading Corp.", null, "000-111-222-00000", "Pasig", "SALES INVOICE", null, null, null);

    private static InvoiceLine Line(string name, decimal qty, decimal price, TaxType tax = TaxType.Vatable, bool eligible = false) =>
        new(name, "pc", new Quantity(qty), new Money(price), tax, eligible);

    private static Invoice Invoice(TaxPolicy? policy = null, params InvoiceLine[] lines) => Discounted(policy, null, lines);

    private static Invoice Discounted(TaxPolicy? policy, SpecialDiscount? discount, params InvoiceLine[] lines) => Domain.Invoice.Issue("SI-1", Seller,
        shipmentId: 1, salesOrderId: 1, "SO-1", customerId: 7, "Acme", "123-456-789-00000", "Makati", paymentTermsDays: 30,
        lines.Length > 0 ? lines : [Line("Widget", 2, 25), new InvoiceLine("Wire", "kg", new Quantity(1.5m), new Money(620.50m), TaxType.Vatable)],
        Issued, policy ?? Vat12, discount);

    private static void Pay(Invoice invoice, long id, decimal amount, DateOnly? on = null, decimal withheld = 0) =>
        invoice.RecordPayment(new PaymentId(id), new Money(amount), new Money(withheld), on ?? Issued, PaymentMethod.Cash, null,
            Accountant, Issued, Now);

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

        invoice.DomainEvents.Should().Equal(
            new InvoicePaymentRecorded(new PaymentId(1), 7, new Money(500)), new InvoicePaymentRecorded(new PaymentId(2), 7, new Money(480.75m)));
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
        invoice.DomainEvents[^1].Should().Be(new InvoicePaymentVoided(new PaymentId(1), 7, new Money(980.75m)));
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
    public void The_VAT_split_adds_back_to_the_total_and_is_kept_on_the_invoice()
    {
        var invoice = Invoice();
        invoice.VatableSales.Should().Be(new Money(875.67m));
        invoice.VatAmount.Should().Be(new Money(105.08m));
        (invoice.VatableSales + invoice.VatAmount).Should().Be(invoice.Total);
        invoice.VatRate.Should().Be(0.12m);
    }

    [Fact]
    public void Mixed_sales_are_broken_down_into_vatable_zero_rated_and_exempt()
    {
        var invoice = Invoice(Vat12, Line("Widget", 1, 112), Line("Export crate", 1, 50, TaxType.ZeroRated), Line("Book", 1, 30, TaxType.VatExempt));

        invoice.VatableSales.Should().Be(new Money(100));
        invoice.VatAmount.Should().Be(new Money(12));
        invoice.ZeroRatedSales.Should().Be(new Money(50));
        invoice.VatExemptSales.Should().Be(new Money(30));
        invoice.Total.Should().Be(new Money(192));
    }

    [Fact]
    public void The_rate_comes_from_the_policy_and_a_non_vat_seller_charges_no_vat()
    {
        Invoice(new TaxPolicy(TaxRegime.Vat, 0.10m), Line("Widget", 1, 110)).VatAmount.Should().Be(new Money(10));

        var nonVat = Invoice(new TaxPolicy(TaxRegime.PercentageTax, 0.12m), Line("Widget", 1, 110), Line("Book", 1, 30, TaxType.VatExempt));
        nonVat.VatAmount.Should().Be(Money.Zero);
        nonVat.VatRate.Should().Be(0);
        nonVat.VatExemptSales.Should().Be(new Money(30));

        FluentActions.Invoking(() => new TaxPolicy(TaxRegime.Vat, 1.2m)).Should().Throw<DomainException>();
    }

    [Fact]
    public void A_senior_discount_moves_the_sale_to_VAT_exempt_and_shows_both_deductions()
    {
        var senior = SpecialDiscount.Grant("Senior citizen", "OSCA ID No.", 0.20m, vatExempt: true, "Lola Basyang", "OSCA-0001", Vat12);

        var invoice = Discounted(Vat12, senior, Line("Medicine", 1, 1120, eligible: true), Line("Widget", 1, 112));

        invoice.SpecialDiscountVatExemption.Should().Be(new Money(120));
        invoice.SpecialDiscountAmount.Should().Be(new Money(200));
        invoice.VatableSales.Should().Be(new Money(100));
        invoice.VatAmount.Should().Be(new Money(12));
        invoice.VatExemptSales.Should().Be(new Money(1000));
        invoice.Total.Should().Be(new Money(912), "the same total Sales charged the customer for the order");
    }

    [Fact]
    public void Tax_withheld_settles_the_invoice_like_money_and_comes_back_if_the_payment_is_voided()
    {
        var invoice = Invoice();   // ₱980.75, of which ₱875.67 before VAT

        Pay(invoice, 1, 972.00m, withheld: 8.75m);   // 1% of the sales before VAT, certified later on a Form 2307

        invoice.Status.Should().Be(InvoiceStatus.Paid);
        invoice.DomainEvents.OfType<InvoicePaymentRecorded>().Single().Settled.Should().Be(new Money(980.75m));

        invoice.VoidPayment(new PaymentId(1), "Wrong invoice", Now);
        invoice.BalanceDue.Should().Be(new Money(980.75m));
    }

    [Fact]
    public void Tax_withheld_cannot_be_more_than_the_sales_before_VAT() =>
        Invoice().Invoking(i => Pay(i, 1, 0, withheld: 900m)).Should().Throw<DomainException>().WithMessage("*before VAT*");

    [Fact]
    public void A_withholding_certificate_covers_one_real_quarter_and_is_voided_once()
    {
        FluentActions.Invoking(() => WithholdingCertificate.Record(7, "Acme", 2026, 5, new Money(10), null, Issued, Accountant, Issued, Now))
            .Should().Throw<DomainException>().WithMessage("*quarter*");
        FluentActions.Invoking(() => WithholdingCertificate.Record(7, "Acme", 2026, 4, Money.Zero, null, Issued, Accountant, Issued, Now))
            .Should().Throw<DomainException>();

        var certificate = WithholdingCertificate.Record(7, "Acme", 2026, 4, new Money(8.75m), "2307-Q4", Issued, Accountant, Issued, Now);
        certificate.Void("Duplicate", Now);
        certificate.Invoking(c => c.Void("Again", Now)).Should().Throw<DomainException>().WithMessage("*already void*");
    }

    private static CompanyProfile Filled(string tin = "000-111-222-00000", string title = "SALES INVOICE", string? dateIssued = null)
    {
        var profile = CompanyProfile.Blank();
        profile.Update("Demo Trading Corp.", null, tin, "Pasig", Vat12, title, null, null, dateIssued, Accountant, Now);
        return profile;
    }

    [Fact]
    public void A_blank_company_profile_stops_invoicing_until_it_is_filled_in()
    {
        CompanyProfile.Blank().Invoking(p => p.EnsureReadyToInvoice()).Should().Throw<DomainException>().WithMessage("*TIN*");
        Filled().Invoking(p => p.EnsureReadyToInvoice()).Should().NotThrow();
    }

    [Fact]
    public void The_company_profile_refuses_what_the_BIR_would_refuse()
    {
        FluentActions.Invoking(() => Filled(tin: "000-111-222")).Should().Throw<DomainException>().WithMessage("*branch code*");
        FluentActions.Invoking(() => Filled(title: "RECEIPT")).Should().Throw<DomainException>().WithMessage("*Invoice*");
        FluentActions.Invoking(() => Filled(dateIssued: "2026-11-06")).Should().Throw<DomainException>().WithMessage("*mm/dd/yyyy*");
        Filled(title: "charge invoice").InvoiceTitle.Should().Be("CHARGE INVOICE");
    }

    [Fact]
    public void An_invoice_keeps_the_seller_it_was_issued_with()
    {
        var profile = Filled();
        var invoice = Domain.Invoice.Issue("SI-1", profile.Seller(), 1, 1, "SO-1", 7, "Acme", null, "Makati", 30,
            [Line("Widget", 1, 112)], Issued, profile.Tax);

        profile.Update("Demo Trading Corp.", null, "000-111-222-00000", "Makati (new office)", Vat12, "SALES INVOICE", null, null, null,
            Accountant, Now);

        invoice.Seller.Address.Should().Be("Pasig");
    }
}
