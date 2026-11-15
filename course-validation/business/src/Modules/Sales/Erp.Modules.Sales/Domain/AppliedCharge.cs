using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

// A debt Invoicing told us about that no order of ours created (an opening balance), added to the customer's balance
// exactly once. Like AppliedPayment, the row makes a repeated event a no-op.
internal sealed class AppliedCharge
{
    public string DocumentNumber { get; private set; } = default!;   // Invoicing's OB-… number
    public CustomerId CustomerId { get; private set; }
    public Money Amount { get; private set; }
    public DateTime AppliedUtc { get; private set; }
    public DateTime? ReversedUtc { get; private set; }

    private AppliedCharge() { }

    public static AppliedCharge Apply(string documentNumber, Customer customer, Money amount, DateTime nowUtc)
    {
        customer.AddToBalance(amount);
        return new AppliedCharge { DocumentNumber = documentNumber, CustomerId = customer.Id, Amount = amount, AppliedUtc = nowUtc };
    }

    public void Reverse(Customer customer, DateTime nowUtc)
    {
        if (ReversedUtc is not null) return;   // already reversed: a repeated event
        customer.DeductFromBalance(Amount);
        ReversedUtc = nowUtc;
    }
}
