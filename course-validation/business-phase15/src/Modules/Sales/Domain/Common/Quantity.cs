using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

// A cross-cutting value object: rejects non-positive values in its constructor.
public readonly record struct Quantity
{
    public int Value { get; }
    public Quantity(int value) =>
        Value = value > 0 ? value : throw new DomainException("Quantity must be greater than zero.");
}
