using System.Text.Json.Serialization;

namespace Erp.Shared.BuildingBlocks;

// Greater than zero, at most three decimal places (2.5 kg). Whether fractions are allowed at all depends on the
// item's unit, which only the item knows.
public readonly record struct Quantity
{
    public decimal Value { get; }

    [JsonConstructor]   // see Money
    public Quantity(decimal value)
    {
        if (value <= 0) throw new DomainException("Quantity must be greater than zero.");
        if (decimal.Round(value, 3) != value) throw new DomainException("Quantity allows at most three decimal places.");
        Value = value;
    }

    public bool IsWhole => decimal.Truncate(Value) == Value;

    public static Quantity operator +(Quantity a, Quantity b) => new(a.Value + b.Value);
    public static bool operator >(Quantity a, Quantity b) => a.Value > b.Value;
    public static bool operator <(Quantity a, Quantity b) => a.Value < b.Value;
    public override string ToString() => Value.ToString("0.###");
}
