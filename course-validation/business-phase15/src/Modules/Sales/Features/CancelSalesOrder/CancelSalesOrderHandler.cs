using Erp.Modules.Sales.Domain;
using Erp.Modules.Inventory.Contracts;   // Phase 15: the contract now lives in the Inventory assembly
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Features;

public sealed record CancelSalesOrderCommand(long OrderId) : ICommand<Unit>;

public sealed class CancelSalesOrderHandler(
    ISalesOrderRepository orders,
    ICustomerRepository customers,  // needed to reverse the balance if the order was already Approved
    IStockReservation stock,        // the Inventory contract — release goes back across the wall
    IDbConnectionFactory factory,
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

        using var conn = factory.Create();
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        try
        {
            // If the order was holding stock (submitted/approved), give it back — same transaction.
            if (hadReservation)
                foreach (var line in order.Lines)
                    await stock.ReleaseAsync(line.ItemId.Value, line.Quantity.Value, tx, ct);

            // If we had already charged the customer at approval, reverse it now (a matched debit/credit pair).
            if (wasApproved)
                await customers.AdjustBalanceAsync(order.CustomerId, -total.Amount, tx, ct);

            await orders.UpdateStatusAsync(order, tx, ct);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        await events.DispatchAsync(order, ct);
        return Unit.Value;
    }
}
