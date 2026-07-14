using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

// Cross-cutting value objects (Phase 5 §3a): compared by value, immutable, invalid states unrepresentable.

public readonly record struct Quantity
{
    public int Value { get; }
    public Quantity(int value) =>
        Value = value > 0 ? value : throw new DomainException("Quantity must be greater than zero.");
}

// ₱ — a plain non-negative amount.
public readonly record struct Money
{
    public decimal Amount { get; }
    public static readonly Money Zero = new(0m);
    public Money(decimal amount) =>
        Amount = amount >= 0 ? amount : throw new DomainException("Money cannot be negative.");

    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount);
    public static Money operator *(Money m, int qty) => new(m.Amount * qty);
    public static bool operator >(Money a, Money b) => a.Amount > b.Amount;
    public static bool operator <(Money a, Money b) => a.Amount < b.Amount;
    public static bool operator >=(Money a, Money b) => a.Amount >= b.Amount;
    public static bool operator <=(Money a, Money b) => a.Amount <= b.Amount;
}
