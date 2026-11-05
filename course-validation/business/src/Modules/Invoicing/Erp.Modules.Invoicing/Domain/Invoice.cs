using Erp.Modules.Invoicing.Contracts;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Invoicing.Domain;

internal readonly record struct InvoiceId(long Value);
internal readonly record struct PaymentId(long Value);
internal readonly record struct UserId(long Value);   // owned by Identity

// Stored as numbers: never renumber.
internal enum InvoiceStatus { Issued = 0, PartiallyPaid = 1, Paid = 2, Void = 3 }
internal enum PaymentMethod { Cash = 1, Check = 2, BankTransfer = 3, Other = 4 }

// What was billed, as it was billed: nothing here follows later changes to the customer or the item.
internal sealed class InvoiceLine
{
    public string Description { get; private set; } = default!;
    public string Unit { get; private set; } = default!;
    public Quantity Quantity { get; private set; }
    public Money UnitPrice { get; private set; }
    public Money LineTotal => UnitPrice * Quantity;

    private InvoiceLine() { }

    public InvoiceLine(string description, string unit, Quantity quantity, Money unitPrice)
    { Description = description; Unit = unit; Quantity = quantity; UnitPrice = unitPrice; }
}

internal sealed class Payment
{
    public PaymentId Id { get; private set; }
    public Money Amount { get; private set; }
    public DateOnly PaidOn { get; private set; }
    public PaymentMethod Method { get; private set; }
    public string? Reference { get; private set; }   // check number, bank reference
    public UserId RecordedBy { get; private set; }
    public DateTime RecordedUtc { get; private set; }
    public DateTime? VoidedUtc { get; private set; }
    public string? VoidReason { get; private set; }
    public bool IsVoid => VoidedUtc is not null;

    private Payment() { }

    public Payment(PaymentId id, Money amount, DateOnly paidOn, PaymentMethod method, string? reference, UserId by, DateTime nowUtc)
    { Id = id; Amount = amount; PaidOn = paidOn; Method = method; Reference = reference; RecordedBy = by; RecordedUtc = nowUtc; }

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
    public Money AmountPaid { get; private set; }
    public string? VoidReason { get; private set; }
    public UserId? VoidedBy { get; private set; }
    public DateTime? VoidedUtc { get; private set; }

    public Money BalanceDue => Total - AmountPaid;

    private Invoice() { }

    public static Invoice Issue(string number, long shipmentId, long salesOrderId, string orderNumber, long customerId,
        string customerName, string? customerTin, string? customerAddress, int paymentTermsDays,
        IReadOnlyList<InvoiceLine> lines, DateOnly issueDate)
    {
        if (lines.Count == 0) throw new DomainException("An invoice needs at least one line.");
        var invoice = new Invoice
        {
            Number = number, ShipmentId = shipmentId, SalesOrderId = salesOrderId, OrderNumber = orderNumber,
            CustomerId = customerId, CustomerName = customerName, CustomerTin = customerTin, CustomerAddress = customerAddress,
            IssueDate = issueDate, DueDate = issueDate.AddDays(paymentTermsDays), Status = InvoiceStatus.Issued,
            AmountPaid = Money.Zero
        };
        invoice._lines.AddRange(lines);
        invoice.Total = lines.Aggregate(Money.Zero, (sum, l) => sum + l.LineTotal);
        return invoice;
    }

    public void RecordPayment(PaymentId id, Money amount, DateOnly paidOn, PaymentMethod method, string? reference,
        UserId by, DateOnly today, DateTime nowUtc)
    {
        EnsureNotVoid();
        if (amount == Money.Zero) throw new DomainException("A payment must be more than zero.");
        if (amount > BalanceDue)
            throw new DomainException($"{Number} has {BalanceDue} left to pay; {amount} is more than that.");
        if (paidOn < IssueDate) throw new DomainException("A payment can't be dated before the invoice.");
        if (paidOn > today) throw new DomainException("A payment can't be dated in the future.");

        _payments.Add(new Payment(id, amount, paidOn, method, string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(), by, nowUtc));
        AmountPaid += amount;
        RefreshStatus();
        Raise(new PaymentRecorded(id.Value, CustomerId, amount.Amount));
    }

    // A payment recorded by mistake, or a check that bounced.
    public void VoidPayment(PaymentId id, string reason, DateTime nowUtc)
    {
        EnsureNotVoid();
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("Voiding a payment needs a reason.");
        var payment = _payments.SingleOrDefault(p => p.Id == id) ?? throw new DomainException($"Payment {id.Value} is not on {Number}.");
        if (payment.IsVoid) throw new DomainException("That payment is already void.");

        payment.Void(reason.Trim(), nowUtc);
        AmountPaid -= payment.Amount;
        RefreshStatus();
        Raise(new PaymentVoided(id.Value, CustomerId, payment.Amount.Amount));
    }

    public void Void(string reason, UserId by, DateTime nowUtc)
    {
        EnsureNotVoid();
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("Voiding an invoice needs a reason.");
        if (AmountPaid > Money.Zero) throw new DomainException($"{Number} has payments. Void them first.");
        Status = InvoiceStatus.Void;
        VoidReason = reason.Trim();
        VoidedBy = by;
        VoidedUtc = nowUtc;
    }

    public void EnsureCanBeReissued()
    {
        if (Status != InvoiceStatus.Void) throw new DomainException($"Only a void invoice can be reissued; {Number} is {Status}.");
    }

    private void RefreshStatus() =>
        Status = BalanceDue == Money.Zero ? InvoiceStatus.Paid
            : AmountPaid > Money.Zero ? InvoiceStatus.PartiallyPaid
            : InvoiceStatus.Issued;

    private void EnsureNotVoid()
    {
        if (Status == InvoiceStatus.Void) throw new DomainException($"{Number} is void.");
    }
}

// The VAT split printed on the invoice. Prices are VAT-inclusive; a non-VAT seller shows no split.
internal static class VatBreakdown
{
    public const decimal Rate = 0.12m;

    public static (Money Vatable, Money Vat) Of(Money total) =>
        (new Money(total.Amount / (1 + Rate)), new Money(total.Amount - Math.Round(total.Amount / (1 + Rate), 2, MidpointRounding.AwayFromZero)));
}
