using System.Text.Json.Serialization;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Inventory.Domain;

// What one unit of stock cost. Four decimals, not two: an average of ₱10.00 and ₱10.01 must not drift a centavo per
// receipt. Amounts of money (a line's value) round to two when multiplied out.
internal readonly record struct UnitCost
{
    public static readonly UnitCost Zero = new(0m);

    public decimal Value { get; }

    [JsonConstructor]   // it travels in ShipmentCreated: without this the outbox would bring it back as 0
    public UnitCost(decimal value) =>
        Value = value >= 0
            ? Math.Round(value, 4, MidpointRounding.AwayFromZero)
            : throw new DomainException(InventoryErrors.NegativeCost, "A cost can't be negative.");

    public decimal ValueOf(decimal quantity) => Math.Round(Value * quantity, 2, MidpointRounding.AwayFromZero);

    // Moving average: what's on hand and what arrives, weighted by quantity. With nothing on hand, the new cost.
    public static UnitCost Average(decimal onHand, UnitCost current, decimal arriving, UnitCost cost) =>
        onHand <= 0 ? cost : new((onHand * current.Value + arriving * cost.Value) / (onHand + arriving));
}
