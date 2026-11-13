using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

internal sealed class Customer : Entity<CustomerId>
{
    public string Name { get; private set; } = default!;
    public string? Tin { get; private set; }         // BIR Taxpayer Identification Number, printed on invoices
    public string? Address { get; private set; }
    public int PaymentTermsDays { get; private set; }
    public bool IsBlocked { get; private set; }
    public Money CreditLimit { get; private set; }
    public Money OutstandingBalance { get; private set; }   // approved orders not yet paid for

    private Customer() { }

    public static Customer Create(string name, string? tin, string? address, Money creditLimit, int paymentTermsDays)
    {
        var customer = new Customer();
        customer.Update(name, tin, address, creditLimit, paymentTermsDays);
        return customer;
    }

    public void Update(string name, string? tin, string? address, Money creditLimit, int paymentTermsDays)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException(SalesErrors.CustomerNameRequired, "A customer needs a name.");
        if (paymentTermsDays is < 0 or > 365) throw new DomainException(SalesErrors.PaymentTermsOutOfRange, "Payment terms must be between 0 and 365 days.");
        Name = name.Trim();
        Tin = string.IsNullOrWhiteSpace(tin) ? null : tin.Trim();
        Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        CreditLimit = creditLimit;
        PaymentTermsDays = paymentTermsDays;
    }

    public void Block() => IsBlocked = true;
    public void Unblock() => IsBlocked = false;

    public void EnsureNotBlocked()
    {
        if (IsBlocked)
            throw new DomainException(SalesErrors.CustomerBlocked, $"Customer '{Name}' is blocked and cannot place orders.");
    }

    // Exposure = what they owe + what's awaiting approval + this order.
    public void EnsureWithinCreditLimit(Money orderTotal, Money otherPendingTotal)
    {
        if (OutstandingBalance + otherPendingTotal + orderTotal <= CreditLimit) return;

        var available = Math.Max(0, CreditLimit.Amount - OutstandingBalance.Amount - otherPendingTotal.Amount);
        throw new DomainException(SalesErrors.CreditLimitExceeded,
            $"Order total ₱{orderTotal.Amount:N2} would exceed the credit limit. Available credit: ₱{available:N2}.");
    }

    public void AddToBalance(Money amount) => OutstandingBalance += amount;

    // A cancelled order, or money received. Never below zero: an overpayment is the accountant's to sort out.
    public void DeductFromBalance(Money amount) =>
        OutstandingBalance = OutstandingBalance - Money.Min(amount, OutstandingBalance);
}
