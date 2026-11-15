using AwesomeAssertions;
using Erp.Modules.Invoicing.Domain;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Invoicing.UnitTests;

// A credit memo takes back what an invoice billed: what's still owed first, then a refund for what was already paid.
public class CreditMemoTests
{
    private static readonly DateTime Now = new(2026, 11, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 11, 15);
    private static readonly UserId Accountant = new(6);
    private static readonly SellerDetails Seller = new("Demo Trading Corp.", null, "000-111-222-00000", "Pasig", "SALES INVOICE", null, null, null);

    // Lines get their ids from the database; here they are given by hand.
    private static InvoiceLine Line(long id, string name, decimal qty, decimal price, TaxType tax = TaxType.Vatable, bool eligible = false,
        long? itemId = null, decimal? cost = null)
    {
        var line = new InvoiceLine(name, "pc", new Quantity(qty), new Money(price), tax, eligible, itemId ?? id, cost);
        typeof(InvoiceLine).GetProperty(nameof(InvoiceLine.Id))!.SetValue(line, id);
        return line;
    }

    private static Invoice Issue(SpecialDiscount? discount = null, params InvoiceLine[] lines) => Invoice.Issue("SI-1", Seller, 1, 1, "SO-1", 7,
        "Acme", null, null, 30, lines.Length > 0 ? lines : [Line(1, "Widget", 10, 112, cost: 70), Line(2, "Gadget", 2, 56)], Today,
        new TaxPolicy(TaxRegime.Vat, 0.12m), discount);

    private static CreditMemo Credit(Invoice invoice, params (long Line, decimal Qty)[] lines) =>
        invoice.Credit("CM-1", lines.Select(l => (l.Line, l.Qty)).ToList(), "Returned unopened", returnToStock: true, Accountant, Today, Now);

    [Fact]
    public void Part_of_a_line_is_credited_with_its_own_vat()
    {
        var invoice = Issue();   // ₱1,232

        var memo = Credit(invoice, (1, 3));   // 3 × ₱112 = ₱336, VAT inside: ₱36

        memo.Total.Should().Be(new Money(336));
        (memo.VatableSales, memo.VatAmount).Should().Be((new Money(300), new Money(36)));
        invoice.BalanceDue.Should().Be(new Money(896));
        invoice.Lines.Single(l => l.Id == 1).Remaining.Should().Be(7);
    }

    [Fact]
    public void What_was_already_paid_becomes_a_refund()
    {
        var invoice = Issue();
        invoice.RecordPayment(new PaymentId(1), new Money(1_000), Money.Zero, Today, PaymentMethod.Cash, null, Accountant, Today, Now);   // ₱232 left

        var memo = Credit(invoice, (1, 3));   // ₱336

        (memo.AppliedToInvoice, memo.RefundDue).Should().Be((new Money(232), new Money(104)));
        invoice.BalanceDue.Should().Be(Money.Zero);
        invoice.Status.Should().Be(InvoiceStatus.Paid);
        memo.DomainEvents.Should().ContainSingle().Which.Should().Be(new CreditMemoCreated("CM-1", 7, new Money(232)));
    }

    [Fact]
    public void Crediting_everything_unpaid_marks_the_invoice_credited()
    {
        var invoice = Issue();
        Credit(invoice, (1, 10), (2, 2));
        invoice.Status.Should().Be(InvoiceStatus.Credited);
    }

    [Fact]
    public void Earlier_credits_count_and_more_than_billed_is_refused()
    {
        var invoice = Issue();
        Credit(invoice, (1, 8));

        invoice.Invoking(i => Credit(i, (1, 3))).Should().Throw<DomainException>().Which.Error.Should().Be(InvoicingErrors.CreditExceedsInvoiced);
        invoice.Lines.Single(l => l.Id == 1).Remaining.Should().Be(2, "the refused credit changed nothing");
    }

    [Fact]
    public void Each_rule_has_its_code()
    {
        var invoice = Issue();
        invoice.Invoking(i => i.Credit("CM-1", [(1, 1)], " ", false, Accountant, Today, Now))
            .Should().Throw<DomainException>().Which.Error.Should().Be(InvoicingErrors.CreditReasonRequired);
        invoice.Invoking(i => Credit(i)).Should().Throw<DomainException>().Which.Error.Should().Be(InvoicingErrors.NothingToCredit);
        invoice.Invoking(i => Credit(i, (9, 1))).Should().Throw<DomainException>().Which.Error.Should().Be(InvoicingErrors.LineNotOnInvoice);
    }

    [Fact]
    public void A_discounted_invoice_is_credited_whole_and_the_memo_mirrors_it()
    {
        var senior = SpecialDiscount.Grant("Senior citizen", "OSCA ID No.", 0.20m, vatExempt: true, "Lola Basyang", "OSCA-1",
            new TaxPolicy(TaxRegime.Vat, 0.12m));
        var invoice = Issue(senior, Line(1, "Rice", 2, 112, eligible: true));

        invoice.Invoking(i => Credit(i, (1, 1))).Should().Throw<DomainException>()
            .Which.Error.Should().Be(InvoicingErrors.DiscountedInvoiceCreditedWhole);
        var memo = Credit(invoice, (1, 2));

        memo.Total.Should().Be(invoice.Total);
        (memo.VatExemptSales, memo.SpecialDiscountAmount).Should().Be((invoice.VatExemptSales, invoice.SpecialDiscountAmount));
    }

    [Fact]
    public void An_invoice_with_credits_is_not_voided()
    {
        var invoice = Issue();
        Credit(invoice, (2, 1));
        invoice.Invoking(i => i.Void("Wrong", Accountant, Now)).Should().Throw<DomainException>()
            .Which.Error.Should().Be(InvoicingErrors.InvoiceHasCredits);
    }

    [Fact]
    public void Goods_go_back_on_the_shelf_at_what_they_cost_only_when_they_came_back()
    {
        var back = Credit(Issue(), (1, 3));
        back.StockToReturn().Should().Equal((1L, 3m, (decimal?)70m));

        var priceFix = Issue().Credit("CM-2", [(1, 3)], "Wrong price", returnToStock: false, Accountant, Today, Now);
        priceFix.StockToReturn().Should().BeEmpty();
    }

    [Fact]
    public void A_refund_is_recorded_once_and_only_when_one_is_due()
    {
        var invoice = Issue();
        Credit(invoice, (2, 1)).Invoking(m => m.RecordRefund(Today, PaymentMethod.Cash, null, Today))
            .Should().Throw<DomainException>().Which.Error.Should().Be(InvoicingErrors.NoRefundDue);

        invoice.RecordPayment(new PaymentId(1), new Money(1_176), Money.Zero, Today, PaymentMethod.Cash, null, Accountant, Today, Now);   // paid in full
        var memo = invoice.Credit("CM-2", [(1, 1)], "Returned", true, Accountant, Today, Now);
        memo.RecordRefund(Today, PaymentMethod.Cash, "OR-55", Today);
        memo.Invoking(m => m.RecordRefund(Today, PaymentMethod.Cash, null, Today))
            .Should().Throw<DomainException>().Which.Error.Should().Be(InvoicingErrors.RefundAlreadyRecorded);
    }
}
