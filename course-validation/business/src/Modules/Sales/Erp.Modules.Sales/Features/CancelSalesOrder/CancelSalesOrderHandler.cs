using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Erp.Modules.Sales.Features;

internal sealed record CancelSalesOrderCommand(long OrderId) : ICommand;

internal sealed class CancelSalesOrderHandler(SalesDbContext db, IStockReservation stock, IPublisher publisher)
    : ICommandHandler<CancelSalesOrderCommand>
{
    public async ValueTask<Unit> Handle(CancelSalesOrderCommand cmd, CancellationToken ct)
    {
        var orderId = new SalesOrderId(cmd.OrderId);
        var order = await db.SalesOrders.SingleOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new NotFoundException($"Sales order {cmd.OrderId} not found.");

        // Capture what must be undone BEFORE the status changes.
        var heldStock = order.Status is SalesOrderStatus.PendingApproval or SalesOrderStatus.Approved;
        var wasCharged = order.Status == SalesOrderStatus.Approved;

        order.Cancel();   // domain: refuses if cancelling isn't legal from here

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (heldStock)
            foreach (var line in order.Lines)
                await stock.ReleaseAsync(line.ItemId.Value, line.Quantity.Value, tx.GetDbTransaction(), ct);

        if (wasCharged)
        {
            var customer = await db.Customers.SingleAsync(c => c.Id == order.CustomerId, ct);
            customer.DeductFromBalance(order.Total);   // reverse the charge made at approval
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await publisher.PublishDomainEventsAsync(order, ct);
        return Unit.Value;
    }
}
