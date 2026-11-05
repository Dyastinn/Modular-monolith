using Erp.Modules.Inventory.Contracts;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Inventory.Domain;

internal readonly record struct ShipmentId(long Value);

internal sealed record ShipmentLine(ItemId ItemId, decimal Quantity);

// Goods that left the building for one approved sales order. Invoicing bills from it.
internal sealed class Shipment : AggregateRoot<ShipmentId>
{
    private readonly List<ShipmentLine> _lines = new();
    public IReadOnlyList<ShipmentLine> Lines => _lines;

    public string Number { get; private set; } = default!;
    public long SalesOrderId { get; private set; }   // owned by Sales: id only, no foreign key
    public string OrderNumber { get; private set; } = default!;
    public UserId ShippedBy { get; private set; }
    public DateTime ShippedUtc { get; private set; }

    private Shipment() { }

    public static Shipment Record(ShipmentId id, string number, long salesOrderId, string orderNumber,
        IEnumerable<ShipmentLine> lines, UserId by, DateTime nowUtc)
    {
        var shipment = new Shipment
        {
            Id = id, Number = number, SalesOrderId = salesOrderId, OrderNumber = orderNumber, ShippedBy = by, ShippedUtc = nowUtc
        };
        shipment._lines.AddRange(lines);
        if (shipment._lines.Count == 0) throw new DomainException("A shipment needs at least one line.");
        shipment.Raise(new ShipmentRecorded(id.Value, number, salesOrderId, nowUtc));
        return shipment;
    }
}
