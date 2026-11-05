using System.Text.Json.Serialization;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

// Philippine pesos, never negative.
internal readonly record struct Money
{
    public static readonly Money Zero = new(0m);

    public decimal Amount { get; }

    [JsonConstructor]   // without it, JSON (the outbox) builds the struct empty and the amount comes back as 0
    public Money(decimal amount) =>
        Amount = amount >= 0 ? amount : throw new DomainException("Money cannot be negative.");

    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount);
    public static Money operator *(Money m, int qty) => new(m.Amount * qty);
    public static bool operator >(Money a, Money b) => a.Amount > b.Amount;
    public static bool operator <(Money a, Money b) => a.Amount < b.Amount;
    public static bool operator >=(Money a, Money b) => a.Amount >= b.Amount;
    public static bool operator <=(Money a, Money b) => a.Amount <= b.Amount;
}
