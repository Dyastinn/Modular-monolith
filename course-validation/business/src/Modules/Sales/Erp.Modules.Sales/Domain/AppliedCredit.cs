using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

// A credit memo Invoicing told us about, taken off the customer's balance exactly once (the row makes a repeated event
// a no-op). Like a payment, it never takes the balance below zero.
internal sealed class AppliedCredit
{
    public string DocumentNumber { get; private set; } = default!;   // Invoicing's CM-… number
    public CustomerId CustomerId { get; private set; }
    public Money Amount { get; private set; }   // what was actually deducted
    public DateTime AppliedUtc { get; private set; }

    private AppliedCredit() { }

    public static AppliedCredit Apply(string documentNumber, Customer customer, Money credited, DateTime nowUtc)
    {
        var deducted = Money.Min(credited, customer.OutstandingBalance);
        customer.DeductFromBalance(deducted);
        return new AppliedCredit { DocumentNumber = documentNumber, CustomerId = customer.Id, Amount = deducted, AppliedUtc = nowUtc };
    }
}
