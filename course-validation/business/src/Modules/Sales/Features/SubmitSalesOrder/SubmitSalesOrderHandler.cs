using Erp.Modules.Sales.Domain;
using Erp.Shared.Common;
using Erp.Shared.Common.Data;
using Erp.Shared.Messaging;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Features;

public sealed record SubmitSalesOrderCommand(long OrderId) : ICommand<Unit>;

public sealed class SubmitSalesOrderHandler(
    ISalesOrderRepository orders,
    ICustomerRepository customers,
    IStockReservation stock,        // ← the published Inventory contract, NOT Inventory's internals
    IDbConnectionFactory factory) : ICommandHandler<SubmitSalesOrderCommand, Unit>
{
    public async Task<Unit> Handle(SubmitSalesOrderCommand cmd, CancellationToken ct)
    {
        // 1) Load the aggregate.
        var order = await orders.GetAsync(new SalesOrderId(cmd.OrderId), ct)
            ?? throw new NotFoundException($"Sales order {cmd.OrderId} not found.");

        // 2) Re-check customer rules (state may have changed since the order was created).
        var customer = await customers.GetAsync(order.CustomerId, ct)
            ?? throw new NotFoundException("Customer not found.");
        customer.EnsureNotBlocked();
        customer.EnsureWithinCreditLimit(order.Total);

        // 3) DOMAIN: is submitting legal, and is the order non-empty? Throws if not.
        order.Submit();

        // 4) One transaction wraps EVERYTHING that follows.
        using var conn = factory.Create();
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        try
        {
            // 5) Atomically reserve each line. If ANY fails -> throw -> whole tx rolls back.
            foreach (var line in order.Lines)
            {
                var reserved = await stock.TryReserveAsync(line.ItemId.Value, line.Quantity.Value, tx, ct);
                if (!reserved)
                    throw new DomainException(
                        $"Insufficient stock for item {line.ItemId.Value}. Submission cancelled.");
            }

            // 6) Persist the new status (version-checked).
            await orders.UpdateStatusAsync(order, tx, ct);

            // 7) Commit: reservations + status change become permanent together.
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        return Unit.Value;
    }
}
