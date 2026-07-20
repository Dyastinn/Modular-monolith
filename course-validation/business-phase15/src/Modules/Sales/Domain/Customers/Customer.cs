using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

internal sealed class Customer
{
    public CustomerId Id { get; }
    public string Name { get; }
    public bool IsBlocked { get; }
    public Money CreditLimit { get; }
    public Money OutstandingBalance { get; }

    public Customer(CustomerId id, string name, bool isBlocked, Money creditLimit, Money outstanding)
    { Id = id; Name = name; IsBlocked = isBlocked; CreditLimit = creditLimit; OutstandingBalance = outstanding; }

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
}
