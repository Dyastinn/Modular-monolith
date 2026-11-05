using System.Text.Json.Serialization;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

internal readonly record struct Quantity
{
    public int Value { get; }

    [JsonConstructor]   // see Money
    public Quantity(int value) =>
        Value = value > 0 ? value : throw new DomainException("Quantity must be greater than zero.");
}
