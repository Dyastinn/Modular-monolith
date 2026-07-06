using Erp.Modules.Sales.Domain;
using Erp.Shared.Common;
using Erp.Shared.Infrastructure;
using Erp.Shared.Messaging;

namespace Erp.Modules.Sales.Features;

public sealed record CancelSalesOrderCommand(long OrderId) : ICommand<Unit>;

public sealed class CancelSalesOrderHandler(
    ISalesOrderRepository orders,
    ICustomerRepository customers,  // needed to reverse the balance if the order was already Approved
    IStockReservation stock,        // the Inventory contract — release goes back across the wall
    IUnitOfWork uow,
    IDomainEventDispatcher events) : ICommandHandler<CancelSalesOrderCommand, Unit>
{
    public async Task<Unit> Handle(CancelSalesOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetAsync(new SalesOrderId(cmd.OrderId), ct)
            ?? throw new NotFoundException($"Sales order {cmd.OrderId} not found.");

        // Capture what we must undo BEFORE we change the status.
        var hadReservation = order.Status is SalesOrderStatus.PendingApproval or SalesOrderStatus.Approved;
        var wasApproved = order.Status == SalesOrderStatus.Approved;
        var total = order.Total;

        // DOMAIN: is cancelling legal from the current status? Throws if not (e.g. already Cancelled).
        order.Cancel();

        await using var tx = await uow.BeginAsync(ct);

        // If the order was holding stock (submitted/approved), give it back — same transaction.
        if (hadReservation)
            foreach (var line in order.Lines)
                await stock.ReleaseAsync(line.ItemId.Value, line.Quantity.Value, tx.Transaction, ct);

        // If we had already charged the customer at approval, reverse it now (a matched debit/credit pair).
        if (wasApproved)
            await customers.AdjustBalanceAsync(order.CustomerId, -total.Amount, tx.Transaction, ct);

        await orders.UpdateStatusAsync(order, tx.Transaction, ct);
        await tx.CommitAsync(ct);

        await events.DispatchAsync(order, ct);
        return Unit.Value;
    }
}
