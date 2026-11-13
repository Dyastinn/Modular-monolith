using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Invoicing.Domain;

internal readonly record struct InvoiceId(long Value);
internal readonly record struct PaymentId(long Value);
internal readonly record struct UserId(long Value);   // owned by Identity

// Stored as numbers: never renumber.
internal enum InvoiceStatus { Issued = 0, PartiallyPaid = 1, Paid = 2, Void = 3 }
internal enum PaymentMethod { Cash = 1, Check = 2, BankTransfer = 3, Other = 4 }

// The totals RR 7-2024 Sec. 6(B.13) requires: VATable sales (net of VAT), the VAT, zero-rated and VAT-exempt sales.
// A special discount's VAT exemption moves those sales from VATable to VAT-exempt, net of the VAT (Sec. 6(B.18)).
internal readonly record struct TaxBreakdown(Money VatableSales, Money VatAmount, Money ZeroRatedSales, Money VatExemptSales)
{
    public static TaxBreakdown Of(IReadOnlyList<InvoiceLine> lines, TaxPolicy policy, SpecialDiscountAmounts discount)
    {
        Money Sum(Func<InvoiceLine, bool> include) => lines.Where(include).Aggregate(Money.Zero, (sum, l) => sum + l.LineTotal);
        var exempt = Sum(l => l.TaxType == TaxType.VatExempt);

        if (policy.Regime == TaxRegime.Exempt)
            return new(Money.Zero, Money.Zero, Money.Zero, Sum(_ => true) - discount.VatExemption);
        if (policy.Regime == TaxRegime.PercentageTax)
            return new(Money.Zero, Money.Zero, Money.Zero, exempt);

        // Prices include VAT, so the VAT is taken out of the VATable total once, not line by line.
        var vatableGross = Sum(l => l.TaxType == TaxType.Vatable) - discount.VatExemptedGross;
        var net = new Money(vatableGross.Amount / (1 + policy.VatRate));
        return new(net, vatableGross - net, Sum(l => l.TaxType == TaxType.ZeroRated),
            exempt + discount.VatExemptedGross - discount.VatExemption);
    }
}

// What was billed, as it was billed: nothing here follows later changes to the customer or the item.
internal sealed class InvoiceLine
{
    public string Description { get; private set; } = default!;
    public string Unit { get; private set; } = default!;
    public Quantity Quantity { get; private set; }
    public Money UnitPrice { get; private set; }
    public TaxType TaxType { get; private set; }
    public bool SpecialDiscountEligible { get; private set; }
    public Money LineTotal => UnitPrice * Quantity;

    private InvoiceLine() { }

    public InvoiceLine(string description, string unit, Quantity quantity, Money unitPrice, TaxType taxType,
        bool specialDiscountEligible = false)
    {
        Description = description; Unit = unit; Quantity = quantity; UnitPrice = unitPrice; TaxType = taxType;
        SpecialDiscountEligible = specialDiscountEligible;
    }
}

internal sealed class Payment
{
    public PaymentId Id { get; private set; }
    public Money Amount { get; private set; }         // what came in: cash, check, transfer
    public Money TaxWithheld { get; private set; }    // what the customer kept and will certify on BIR Form 2307
    public Money Settled => Amount + TaxWithheld;
    public DateOnly PaidOn { get; private set; }
    public PaymentMethod Method { get; private set; }
    public string? Reference { get; private set; }   // check number, bank reference
    public UserId RecordedBy { get; private set; }
    public DateTime RecordedUtc { get; private set; }
    public DateTime? VoidedUtc { get; private set; }
    public string? VoidReason { get; private set; }
    public bool IsVoid => VoidedUtc is not null;

    private Payment() { }

    public Payment(PaymentId id, Money amount, Money taxWithheld, DateOnly paidOn, PaymentMethod method, string? reference, UserId by,
        DateTime nowUtc)
    {
        Id = id; Amount = amount; TaxWithheld = taxWithheld; PaidOn = paidOn; Method = method; Reference = reference;
        RecordedBy = by; RecordedUtc = nowUtc;
    }

    public void Void(string reason, DateTime nowUtc) { VoidedUtc = nowUtc; VoidReason = reason; }
}

// An invoice is never deleted or edited. A mistake is voided, with a reason, and a new one issued.
internal sealed class Invoice : AggregateRoot<InvoiceId>
{
    private readonly List<InvoiceLine> _lines = new();
    private readonly List<Payment> _payments = new();
    public IReadOnlyList<InvoiceLine> Lines => _lines;
    public IReadOnlyList<Payment> Payments => _payments;

    public string Number { get; private set; } = default!;
    public SellerDetails Seller { get; private set; } = default!;
    public long ShipmentId { get; private set; }     // owned by Inventory
    public long SalesOrderId { get; private set; }   // owned by Sales
    public string OrderNumber { get; private set; } = default!;
    public long CustomerId { get; private set; }     // owned by Sales
    public string CustomerName { get; private set; } = default!;
    public string? CustomerTin { get; private set; }
    public string? CustomerAddress { get; private set; }
    public DateOnly IssueDate { get; private set; }
    public DateOnly DueDate { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public Money Total { get; private set; }
    public TaxRegime TaxRegime { get; private set; }
    public decimal VatRate { get; private set; }
    public Money VatableSales { get; private set; }
    public Money VatAmount { get; private set; }
    public Money ZeroRatedSales { get; private set; }
    public Money VatExemptSales { get; private set; }
    public SpecialDiscount? SpecialDiscount { get; private set; }
    public Money SpecialDiscountVatExemption { get; private set; }
    public Money SpecialDiscountAmount { get; private set; }
    public Money AmountPaid { get; private set; }   // everything settled: money received plus tax withheld
    public string? VoidReason { get; private set; }
    public UserId? VoidedBy { get; private set; }
    public DateTime? VoidedUtc { get; private set; }

    public Money BalanceDue => Total - AmountPaid;

    private Invoice() { }

    public static Invoice Issue(string number, SellerDetails seller, long shipmentId, long salesOrderId, string orderNumber, long customerId,
        string customerName, string? customerTin, string? customerAddress, int paymentTermsDays,
        IReadOnlyList<InvoiceLine> lines, DateOnly issueDate, TaxPolicy taxPolicy, SpecialDiscount? specialDiscount = null)
    {
        if (lines.Count == 0) throw new DomainException(InvoicingErrors.InvoiceNeedsLines, "An invoice needs at least one line.");
        var invoice = new Invoice
        {
            Number = number, Seller = seller with { }, ShipmentId = shipmentId, SalesOrderId = salesOrderId, OrderNumber = orderNumber,
            CustomerId = customerId, CustomerName = customerName, CustomerTin = customerTin, CustomerAddress = customerAddress,
            IssueDate = issueDate, DueDate = issueDate.AddDays(paymentTermsDays), Status = InvoiceStatus.Issued,
            AmountPaid = Money.Zero
        };
        invoice._lines.AddRange(lines);
        // The same calculation Sales used for the order, so the invoice bills exactly what the customer was charged.
        var discount = specialDiscount?.Apply(lines.Select(l => (l.LineTotal, l.TaxType, l.SpecialDiscountEligible)))
            ?? SpecialDiscountAmounts.None;
        invoice.SpecialDiscount = specialDiscount is null ? null : specialDiscount with { };
        (invoice.SpecialDiscountVatExemption, invoice.SpecialDiscountAmount) = (discount.VatExemption, discount.Discount);
        invoice.Total = lines.Aggregate(Money.Zero, (sum, l) => sum + l.LineTotal) - discount.Total;
        var tax = TaxBreakdown.Of(lines, taxPolicy, discount);
        (invoice.TaxRegime, invoice.VatRate) = (taxPolicy.Regime, taxPolicy.VatRate);
        (invoice.VatableSales, invoice.VatAmount, invoice.ZeroRatedSales, invoice.VatExemptSales) =
            (tax.VatableSales, tax.VatAmount, tax.ZeroRatedSales, tax.VatExemptSales);
        return invoice;
    }

    // taxWithheld: the creditable withholding tax the customer deducted (BIR Form 2307). It settles the invoice like money does.
    public void RecordPayment(PaymentId id, Money amount, Money taxWithheld, DateOnly paidOn, PaymentMethod method, string? reference,
        UserId by, DateOnly today, DateTime nowUtc)
    {
        EnsureNotVoid();
        var settled = amount + taxWithheld;
        if (settled == Money.Zero) throw new DomainException(InvoicingErrors.PaymentNotPositive, "A payment must be more than zero.");
        if (settled > BalanceDue)
            throw new DomainException(InvoicingErrors.PaymentExceedsBalance, $"{Number} has {BalanceDue} left to pay; {settled} is more than that.");
        // Withholding is a few percent of the sale before VAT, so anything near the VAT-inclusive total is a typing mistake.
        if (taxWithheld > Total - VatAmount)
            throw new DomainException(InvoicingErrors.WithholdingExceedsNetSales, $"Tax withheld can't be more than {Number}'s sales before VAT ({Total - VatAmount}).");
        if (paidOn < IssueDate) throw new DomainException(InvoicingErrors.PaymentBeforeInvoice, "A payment can't be dated before the invoice.");
        if (paidOn > today) throw new DomainException(InvoicingErrors.PaymentInFuture, "A payment can't be dated in the future.");

        _payments.Add(new Payment(id, amount, taxWithheld, paidOn, method,
            string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(), by, nowUtc));
        AmountPaid += settled;
        RefreshStatus();
        Raise(new InvoicePaymentRecorded(id, CustomerId, settled));
    }

    // A payment recorded by mistake, or a check that bounced.
    public void VoidPayment(PaymentId id, string reason, DateTime nowUtc)
    {
        EnsureNotVoid();
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(InvoicingErrors.VoidReasonRequired, "Voiding a payment needs a reason.");
        var payment = _payments.SingleOrDefault(p => p.Id == id) ?? throw new DomainException(InvoicingErrors.PaymentNotOnInvoice, $"Payment {id.Value} is not on {Number}.");
        if (payment.IsVoid) throw new DomainException(InvoicingErrors.PaymentAlreadyVoid, "That payment is already void.");

        payment.Void(reason.Trim(), nowUtc);
        AmountPaid -= payment.Settled;
        RefreshStatus();
        Raise(new InvoicePaymentVoided(id, CustomerId, payment.Settled));
    }

    public void Void(string reason, UserId by, DateTime nowUtc)
    {
        EnsureNotVoid();
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(InvoicingErrors.VoidReasonRequired, "Voiding an invoice needs a reason.");
        if (AmountPaid > Money.Zero) throw new DomainException(InvoicingErrors.InvoiceHasPayments, $"{Number} has payments. Void them first.");
        Status = InvoiceStatus.Void;
        VoidReason = reason.Trim();
        VoidedBy = by;
        VoidedUtc = nowUtc;
    }

    public void EnsureCanBeReissued()
    {
        if (Status != InvoiceStatus.Void) throw new DomainException(InvoicingErrors.OnlyVoidInvoiceReissued, $"Only a void invoice can be reissued; {Number} is {Status}.");
    }

    private void RefreshStatus() =>
        Status = BalanceDue == Money.Zero ? InvoiceStatus.Paid
            : AmountPaid > Money.Zero ? InvoiceStatus.PartiallyPaid
            : InvoiceStatus.Issued;

    private void EnsureNotVoid()
    {
        if (Status == InvoiceStatus.Void) throw new DomainException(InvoicingErrors.InvoiceIsVoid, $"{Number} is void.");
    }
}

// Published to other modules as PaymentRecorded / PaymentVoided.
internal sealed record InvoicePaymentRecorded(PaymentId PaymentId, long CustomerId, Money Settled) : IDomainEvent;
internal sealed record InvoicePaymentVoided(PaymentId PaymentId, long CustomerId, Money Settled) : IDomainEvent;
