using System.Text.Json.Serialization;

namespace Erp.Shared.BuildingBlocks;

// Philippine pesos, never negative, always rounded to centavos.
public readonly record struct Money
{
    public static readonly Money Zero = new(0m);

    public decimal Amount { get; }

    [JsonConstructor]   // without it, JSON (the outbox) builds the struct empty and the amount comes back as 0
    public Money(decimal amount) =>
        Amount = amount >= 0
            ? Math.Round(amount, 2, MidpointRounding.AwayFromZero)
            : throw new DomainException(CommonErrors.NegativeMoney, "An amount of money cannot be negative.");

    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount);
    public static Money operator -(Money a, Money b) => new(a.Amount - b.Amount);
    public static Money operator *(Money price, Quantity qty) => new(price.Amount * qty.Value);
    public static bool operator >(Money a, Money b) => a.Amount > b.Amount;
    public static bool operator <(Money a, Money b) => a.Amount < b.Amount;
    public static bool operator >=(Money a, Money b) => a.Amount >= b.Amount;
    public static bool operator <=(Money a, Money b) => a.Amount <= b.Amount;

    public static Money Min(Money a, Money b) => a <= b ? a : b;
    public override string ToString() => $"₱{Amount:N2}";
}
