using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Features;

internal sealed record ApproveSalesOrderCommand(long OrderId) : ICommand<Unit>;

internal sealed class ApproveSalesOrderHandler(
    ISalesOrderRepository orders,
    ICustomerRepository customers,
    IDbConnectionFactory factory,
    ICurrentUser currentUser,
    IDomainEventDispatcher events) : ICommandHandler<ApproveSalesOrderCommand, Unit>
{
    public async Task<Unit> Handle(ApproveSalesOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetAsync(new SalesOrderId(cmd.OrderId), ct)
            ?? throw new NotFoundException($"Sales order {cmd.OrderId} not found.");

        // DOMAIN: only a PendingApproval order can be approved (state machine). Throws otherwise.
        order.Approve(new UserId(currentUser.UserId));

        using var conn = factory.Create();
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        try
        {
            // Version-checked status update: fails if someone changed the order since we loaded it.
            await orders.UpdateStatusAsync(order, tx, ct);

            // The customer now owes this money — add it to their outstanding balance, same transaction.
            await customers.AdjustBalanceAsync(order.CustomerId, +order.Total.Amount, tx, ct);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        // AFTER commit: dispatch domain events (audit log, etc.). Best-effort side effects.
        await events.DispatchAsync(order, ct);
        return Unit.Value;
    }
}
