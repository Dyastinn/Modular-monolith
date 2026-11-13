using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Invoicing.Domain;

internal readonly record struct WithholdingCertificateId(long Value);

// A BIR Form 2307 received from a customer: their proof of the tax they withheld from what they paid us in one quarter.
// The business claims it as a credit against its income tax, so a missing one is money lost.
internal sealed class WithholdingCertificate : Entity<WithholdingCertificateId>
{
    public long CustomerId { get; private set; }   // owned by Sales
    public string CustomerName { get; private set; } = default!;
    public int Year { get; private set; }
    public int Quarter { get; private set; }
    public Money Amount { get; private set; }
    public string? Reference { get; private set; }
    public DateOnly ReceivedOn { get; private set; }
    public UserId RecordedBy { get; private set; }
    public DateTime RecordedUtc { get; private set; }
    public DateTime? VoidedUtc { get; private set; }
    public string? VoidReason { get; private set; }

    private WithholdingCertificate() { }

    public static WithholdingCertificate Record(long customerId, string customerName, int year, int quarter, Money amount,
        string? reference, DateOnly receivedOn, UserId by, DateOnly today, DateTime nowUtc)
    {
        if (quarter is < 1 or > 4) throw new DomainException(InvoicingErrors.InvalidQuarter, "A quarter is 1, 2, 3 or 4.");
        if (year < 2000 || year > today.Year) throw new DomainException(InvoicingErrors.InvalidCertificateYear, $"{year} is not a year a certificate can cover.");
        if (amount == Money.Zero) throw new DomainException(InvoicingErrors.CertificateAmountZero, "A certificate must show some tax withheld.");
        if (receivedOn > today) throw new DomainException(InvoicingErrors.CertificateReceivedInFuture, "A certificate can't be received in the future.");
        return new WithholdingCertificate
        {
            CustomerId = customerId, CustomerName = customerName, Year = year, Quarter = quarter, Amount = amount,
            Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(), ReceivedOn = receivedOn,
            RecordedBy = by, RecordedUtc = nowUtc
        };
    }

    public void Void(string reason, DateTime nowUtc)
    {
        if (VoidedUtc is not null) throw new DomainException(InvoicingErrors.CertificateAlreadyVoid, "That certificate is already void.");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException(InvoicingErrors.VoidReasonRequired, "Voiding a certificate needs a reason.");
        VoidedUtc = nowUtc;
        VoidReason = reason.Trim();
    }
}
