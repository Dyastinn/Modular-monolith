using Erp.Modules.Inventory.Domain;
using Erp.Modules.Inventory.Infrastructure;
using Erp.Modules.Invoicing.Contracts;
using Erp.Modules.Sales.Contracts;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Erp.Modules.Inventory.Features;

// Lines: null = everything still to ship.
internal sealed record ShipSalesOrderCommand(long SalesOrderId, IReadOnlyList<(long ItemId, decimal Quantity)>? Lines = null);

// Stock leaves at its average cost, the reservation is used up, and the order records what shipped: one transaction
// across two modules. Sales refuses a line that isn't on the order or is more than is still to ship.
internal sealed class ShipSalesOrderHandler(InventoryDbContext db, ISalesOrders orders, IInvoicingSetup invoicing, ICurrentUser user,
    TimeProvider time)
{
    public async Task<long> HandleAsync(ShipSalesOrderCommand cmd, CancellationToken ct)
    {
        // The BIR expects the invoice with the goods; refuse now rather than ship something that can't be billed.
        await invoicing.EnsureReadyToInvoiceAsync(ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = await orders.GetForShipmentAsync(cmd.SalesOrderId, tx.GetDbTransaction(), ct)
            ?? throw new DomainException(InventoryErrors.SalesOrderNotFound, $"Sales order {cmd.SalesOrderId} doesn't exist.");

        var now = time.GetUtcNow().UtcDateTime;
        var by = new UserId(user.UserId);
        var id = new ShipmentId(await db.NextShipmentIdAsync(ct));
        var number = DocumentNumber.Format("SH", id.Value, now);

        var toShip = cmd.Lines?.Select(l => new ShippableLine(l.ItemId, l.Quantity)).ToList() ?? order.Lines.ToList();
        // Sales first: a line that doesn't fit the order is refused for that reason, before any stock moves.
        await orders.RecordShipmentAsync(order.OrderId, toShip, now, tx.GetDbTransaction(), ct);

        var lines = new List<ShipmentLine>();
        foreach (var line in toShip)
        {
            var itemId = new ItemId(line.ItemId);
            var rows = await db.Items
                .Where(i => i.Id == itemId && i.Reserved >= line.Quantity && i.OnHand >= line.Quantity)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.OnHand, i => i.OnHand - line.Quantity)
                    .SetProperty(i => i.Reserved, i => i.Reserved - line.Quantity), ct);
            if (rows != 1)
                throw new DomainException(InventoryErrors.ReservationMissing, $"Item {line.ItemId} no longer has {line.Quantity:0.###} reserved for {order.OrderNumber}.");

            // Read after the update, which holds the row: no receipt can change the average before this commits.
            var cost = await db.Items.Where(i => i.Id == itemId).Select(i => i.AverageCost).SingleAsync(ct);
            db.StockMovements.Add(StockMovement.Shipment(itemId, line.Quantity, cost, number, by, now));
            lines.Add(new ShipmentLine(itemId, line.Quantity, cost));
        }

        db.Shipments.Add(Shipment.Record(id, number, order.OrderId, order.OrderNumber, lines, by, now));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return id.Value;
    }
}
