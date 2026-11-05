namespace Erp.Modules.Inventory.Domain;

internal enum MovementKind { Receipt = 1, Adjustment = 2, Shipment = 3 }

// The audit trail: every change to on-hand stock, who made it and why. Written once, never changed.
internal sealed class StockMovement
{
    public long Id { get; private set; }
    public ItemId ItemId { get; private set; }
    public decimal Quantity { get; private set; }   // + in, - out
    public MovementKind Kind { get; private set; }
    public string? Reference { get; private set; }   // PO-..., SH-...
    public string? Reason { get; private set; }
    public UserId UserId { get; private set; }
    public DateTime OccurredUtc { get; private set; }

    private StockMovement() { }

    public static StockMovement Receipt(ItemId item, decimal quantity, string reference, UserId by, DateTime nowUtc) =>
        new() { ItemId = item, Quantity = quantity, Kind = MovementKind.Receipt, Reference = reference, UserId = by, OccurredUtc = nowUtc };

    public static StockMovement Adjustment(ItemId item, decimal delta, string reason, UserId by, DateTime nowUtc) =>
        new() { ItemId = item, Quantity = delta, Kind = MovementKind.Adjustment, Reason = reason, UserId = by, OccurredUtc = nowUtc };

    public static StockMovement Shipment(ItemId item, decimal quantity, string reference, UserId by, DateTime nowUtc) =>
        new() { ItemId = item, Quantity = -quantity, Kind = MovementKind.Shipment, Reference = reference, UserId = by, OccurredUtc = nowUtc };
}
