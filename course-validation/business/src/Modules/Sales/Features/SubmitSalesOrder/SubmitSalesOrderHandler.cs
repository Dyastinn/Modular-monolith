using Erp.Modules.Sales.Domain;
using Erp.Shared.Common;
using Erp.Shared.Infrastructure;
using Erp.Shared.Messaging;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Features;

public sealed record SubmitSalesOrderCommand(long OrderId) : ICommand<Unit>;

public sealed class SubmitSalesOrderHandler(
    ISalesOrderRepository orders,
    ICustomerRepository customers,
    IStockReservation stock,        // ← the published Inventory contract, NOT Inventory's internals
    IUnitOfWork uow) : ICommandHandler<SubmitSalesOrderCommand, Unit>
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
        await using var tx = await uow.BeginAsync(ct);

        // 5) Atomically reserve each line. If ANY fails -> throw -> whole tx rolls back.
        foreach (var line in order.Lines)
        {
            var reserved = await stock.TryReserveAsync(line.ItemId.Value, line.Quantity.Value, tx.Transaction, ct);
            if (!reserved)
                throw new DomainException(
                    $"Insufficient stock for item {line.ItemId.Value}. Submission cancelled.");
        }

        // 6) Persist the new status (version-checked).
        await orders.UpdateStatusAsync(order, tx.Transaction, ct);

        // 7) Commit: reservations + status change become permanent together.
        await tx.CommitAsync(ct);

        return Unit.Value;
    }
}
