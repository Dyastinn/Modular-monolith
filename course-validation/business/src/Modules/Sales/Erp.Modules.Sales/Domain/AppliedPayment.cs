using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

// A payment Invoicing told us about, applied to the customer's balance exactly once. The row is what makes the
// at-least-once event handler idempotent: a second delivery finds it and does nothing.
internal sealed class AppliedPayment
{
    public long PaymentId { get; private set; }
    public CustomerId CustomerId { get; private set; }
    public Money Amount { get; private set; }   // what was actually deducted: less than paid if they overpaid
    public DateTime AppliedUtc { get; private set; }
    public DateTime? ReversedUtc { get; private set; }

    private AppliedPayment() { }

    public static AppliedPayment Apply(long paymentId, Customer customer, Money paid, DateTime nowUtc)
    {
        var deducted = Money.Min(paid, customer.OutstandingBalance);
        customer.DeductFromBalance(deducted);
        return new AppliedPayment { PaymentId = paymentId, CustomerId = customer.Id, Amount = deducted, AppliedUtc = nowUtc };
    }

    public void Reverse(Customer customer, DateTime nowUtc)
    {
        if (ReversedUtc is not null) return;   // already reversed: a repeated event
        customer.AddToBalance(Amount);
        ReversedUtc = nowUtc;
    }
}
