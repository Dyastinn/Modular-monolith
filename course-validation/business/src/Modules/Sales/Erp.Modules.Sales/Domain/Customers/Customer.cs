using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

internal sealed class Customer
{
    public CustomerId Id { get; private set; }
    public string Name { get; private set; } = default!;
    public bool IsBlocked { get; private set; }
    public Money CreditLimit { get; private set; }
    public Money OutstandingBalance { get; private set; }

    private Customer() { }   // for EF

    public Customer(CustomerId id, string name, bool isBlocked, Money creditLimit, Money outstandingBalance)
    { Id = id; Name = name; IsBlocked = isBlocked; CreditLimit = creditLimit; OutstandingBalance = outstandingBalance; }

    public void EnsureNotBlocked()
    {
        if (IsBlocked)
            throw new DomainException($"Customer '{Name}' is blocked and cannot place orders.");
    }

    public void EnsureWithinCreditLimit(Money orderTotal)
    {
        if (OutstandingBalance + orderTotal > CreditLimit)
            throw new DomainException(
                $"Order total ₱{orderTotal.Amount:N2} would exceed the credit limit. " +
                $"Available credit: ₱{(CreditLimit.Amount - OutstandingBalance.Amount):N2}.");
    }

    // Approval commits the customer to paying: the order's total joins what they owe.
    public void AddToBalance(Money amount) => OutstandingBalance += amount;

    // Cancelling an approved order takes the charge back off. Money can't go negative, so over-reversing throws.
    public void DeductFromBalance(Money amount) =>
        OutstandingBalance = new Money(OutstandingBalance.Amount - amount.Amount);
}
