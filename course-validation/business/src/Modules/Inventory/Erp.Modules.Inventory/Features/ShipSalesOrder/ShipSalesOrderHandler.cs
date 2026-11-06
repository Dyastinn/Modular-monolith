using Erp.Modules.Inventory.Domain;
using Erp.Modules.Inventory.Infrastructure;
using Erp.Modules.Invoicing.Contracts;
using Erp.Modules.Sales.Contracts;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Erp.Modules.Inventory.Features;

internal sealed record ShipSalesOrderCommand(long SalesOrderId) : ICommand<long>;

internal sealed class ShipSalesOrderValidator : AbstractValidator<ShipSalesOrderCommand>
{
    public ShipSalesOrderValidator() => RuleFor(x => x.SalesOrderId).GreaterThan(0);
}

// Stock leaves, the reservation is used up, and the order becomes Shipped: one transaction across two modules.
internal sealed class ShipSalesOrderHandler(InventoryDbContext db, ISalesOrders orders, IInvoicingSetup invoicing, ICurrentUser user,
    IClock clock)
    : ICommandHandler<ShipSalesOrderCommand, long>
{
    public async ValueTask<long> Handle(ShipSalesOrderCommand cmd, CancellationToken ct)
    {
        // The BIR expects the invoice with the goods; refuse now rather than ship something that can't be billed.
        await invoicing.EnsureReadyToInvoiceAsync(ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = await orders.GetForShipmentAsync(cmd.SalesOrderId, tx.GetDbTransaction(), ct);

        var now = clock.UtcNow;
        var by = new UserId(user.UserId);
        var id = new ShipmentId(await db.NextShipmentIdAsync(ct));
        var number = DocumentNumber.Format("SH", id.Value, now);

        var lines = new List<ShipmentLine>();
        foreach (var line in order.Lines)
        {
            var itemId = new ItemId(line.ItemId);
            var rows = await db.Items
                .Where(i => i.Id == itemId && i.Reserved >= line.Quantity && i.OnHand >= line.Quantity)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.OnHand, i => i.OnHand - line.Quantity)
                    .SetProperty(i => i.Reserved, i => i.Reserved - line.Quantity), ct);
            if (rows != 1)
                throw new DomainException($"Item {line.ItemId} no longer has {line.Quantity:0.###} reserved for {order.OrderNumber}.");

            db.StockMovements.Add(StockMovement.Shipment(itemId, line.Quantity, number, by, now));
            lines.Add(new ShipmentLine(itemId, line.Quantity));
        }

        db.Shipments.Add(Shipment.Record(id, number, order.OrderId, order.OrderNumber, lines, by, now));
        await db.SaveChangesAsync(ct);
        await orders.MarkShippedAsync(order.OrderId, now, tx.GetDbTransaction(), ct);
        await tx.CommitAsync(ct);
        return id.Value;
    }
}
