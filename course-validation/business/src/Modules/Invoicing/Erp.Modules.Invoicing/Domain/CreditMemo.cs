using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Invoicing.Domain;

internal readonly record struct CreditMemoId(long Value);

// One credited line, as the invoice billed it. ItemId and UnitCost bring returned goods back into stock at what they cost.
internal sealed class CreditMemoLine
{
    public long InvoiceLineId { get; private set; }
    public string Description { get; private set; } = default!;
    public string Unit { get; private set; } = default!;
    public Quantity Quantity { get; private set; }
    public Money UnitPrice { get; private set; }
    public TaxType TaxType { get; private set; }
    public long? ItemId { get; private set; }
    public decimal? UnitCost { get; private set; }
    public Money LineTotal => UnitPrice * Quantity;

    private CreditMemoLine() { }

    public CreditMemoLine(InvoiceLine from, decimal quantity)
    {
        InvoiceLineId = from.Id; Description = from.Description; Unit = from.Unit; Quantity = new Quantity(quantity);
        UnitPrice = from.UnitPrice; TaxType = from.TaxType; ItemId = from.ItemId; UnitCost = from.UnitCost;
    }

    public InvoiceLine AsInvoiceLine() => new(Description, Unit, Quantity, UnitPrice, TaxType);
}

// Takes back part or all of one invoice: goods returned, or a billing mistake. Never edited or deleted. Its tax figures
// are the invoice's own regime and rate applied to what is credited, so the two documents always reconcile.
internal sealed class CreditMemo : AggregateRoot<CreditMemoId>
{
    private readonly List<CreditMemoLine> _lines = new();
    public IReadOnlyList<CreditMemoLine> Lines => _lines;

    public string Number { get; private set; } = default!;
    public SellerDetails Seller { get; private set; } = default!;
    public InvoiceId InvoiceId { get; private set; }
    public string InvoiceNumber { get; private set; } = default!;
    public long CustomerId { get; private set; }
    public string CustomerName { get; private set; } = default!;
    public string? CustomerTin { get; private set; }
    public string? CustomerAddress { get; private set; }
    public DateOnly IssueDate { get; private set; }
    public string Reason { get; private set; } = default!;
    public bool ReturnToStock { get; private set; }
    public TaxRegime TaxRegime { get; private set; }
    public decimal VatRate { get; private set; }
    public Money VatableSales { get; private set; }
    public Money VatAmount { get; private set; }
    public Money ZeroRatedSales { get; private set; }
    public Money VatExemptSales { get; private set; }
    public Money SpecialDiscountVatExemption { get; private set; }
    public Money SpecialDiscountAmount { get; private set; }
    public Money Total { get; private set; }
    public Money AppliedToInvoice { get; private set; }   // taken off what the invoice still owed
    public Money RefundDue { get; private set; }          // the rest: the customer had already paid it
    public DateOnly? RefundedOn { get; private set; }
    public PaymentMethod? RefundMethod { get; private set; }
    public string? RefundReference { get; private set; }
    public UserId CreatedBy { get; private set; }
    public DateTime CreatedUtc { get; private set; }

    private CreditMemo() { }

    // Called by Invoice.Credit, which has already checked every line. whole: the invoice is credited in full, so the
    // memo repeats its figures exactly, special discount and all.
    internal static CreditMemo Issue(string number, Invoice invoice, IReadOnlyList<(InvoiceLine Line, decimal Quantity)> credited, bool whole,
        string reason, bool returnToStock, Money owed, UserId by, DateOnly today, DateTime nowUtc)
    {
        var memo = new CreditMemo
        {
            Number = number, Seller = invoice.Seller with { }, InvoiceId = invoice.Id, InvoiceNumber = invoice.Number,
            CustomerId = invoice.CustomerId, CustomerName = invoice.CustomerName, CustomerTin = invoice.CustomerTin,
            CustomerAddress = invoice.CustomerAddress, IssueDate = today, Reason = reason.Trim(), ReturnToStock = returnToStock,
            TaxRegime = invoice.TaxRegime, VatRate = invoice.VatRate, CreatedBy = by, CreatedUtc = nowUtc
        };
        memo._lines.AddRange(credited.Select(c => new CreditMemoLine(c.Line, c.Quantity)));

        if (whole)
        {
            (memo.VatableSales, memo.VatAmount, memo.ZeroRatedSales, memo.VatExemptSales) =
                (invoice.VatableSales, invoice.VatAmount, invoice.ZeroRatedSales, invoice.VatExemptSales);
            (memo.SpecialDiscountVatExemption, memo.SpecialDiscountAmount) = (invoice.SpecialDiscountVatExemption, invoice.SpecialDiscountAmount);
            memo.Total = invoice.Total;
        }
        else
        {
            var asLines = memo._lines.Select(l => l.AsInvoiceLine()).ToList();
            var tax = TaxBreakdown.Of(asLines, new TaxPolicy(invoice.TaxRegime, invoice.VatRate), SpecialDiscountAmounts.None);
            (memo.VatableSales, memo.VatAmount, memo.ZeroRatedSales, memo.VatExemptSales) =
                (tax.VatableSales, tax.VatAmount, tax.ZeroRatedSales, tax.VatExemptSales);
            (memo.SpecialDiscountVatExemption, memo.SpecialDiscountAmount) = (Money.Zero, Money.Zero);
            memo.Total = asLines.Aggregate(Money.Zero, (sum, l) => sum + l.LineTotal);
        }

        memo.AppliedToInvoice = Money.Min(memo.Total, owed);
        memo.RefundDue = memo.Total - memo.AppliedToInvoice;
        memo.Raise(new CreditMemoCreated(number, memo.CustomerId, memo.AppliedToInvoice));
        return memo;
    }

    // What goes back on the shelf: nothing unless the goods came back fit to sell. A line without an item (an opening
    // balance never gets here) has nothing to return.
    public IReadOnlyList<(long ItemId, decimal Quantity, decimal? UnitCost)> StockToReturn() =>
        ReturnToStock ? _lines.Where(l => l.ItemId is not null).Select(l => (l.ItemId!.Value, l.Quantity.Value, l.UnitCost)).ToList() : [];

    // The money that went back to the customer for what they had already paid.
    public void RecordRefund(DateOnly paidOn, PaymentMethod method, string? reference, DateOnly today)
    {
        if (RefundDue == Money.Zero) throw new DomainException(InvoicingErrors.NoRefundDue, $"{Number} has nothing to refund: it was all taken off the invoice.");
        if (RefundedOn is not null) throw new DomainException(InvoicingErrors.RefundAlreadyRecorded, $"{Number}'s refund was recorded on {RefundedOn:MMMM d, yyyy}.");
        if (paidOn < IssueDate) throw new DomainException(InvoicingErrors.PaymentBeforeInvoice, "A refund can't be dated before its credit memo.");
        if (paidOn > today) throw new DomainException(InvoicingErrors.PaymentInFuture, "A refund can't be dated in the future.");
        (RefundedOn, RefundMethod, RefundReference) = (paidOn, method, string.IsNullOrWhiteSpace(reference) ? null : reference.Trim());
    }
}

// Published to other modules as CreditMemoIssued (Invoicing.Contracts): Sales takes the applied part off the balance.
internal sealed record CreditMemoCreated(string Number, long CustomerId, Money AppliedToInvoice) : IDomainEvent;
