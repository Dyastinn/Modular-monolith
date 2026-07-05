using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;
using Erp.Shared.Common;
using Erp.Shared.Common.Contracts;
using Erp.Shared.Infrastructure.Persistence;
using Erp.Shared.Messaging;

namespace Erp.Modules.Sales.Features.SubmitSalesOrder;

public sealed record SubmitSalesOrderCommand(long SalesOrderId, bool ApprovalGranted) : ICommand<Unit>;

public sealed class SubmitSalesOrderHandler(
    ISalesOrderRepository orders,
    IStockReservation stock,       // ← the published Inventory contract, NOT Inventory's internals
    IUnitOfWork uow) : ICommandHandler<SubmitSalesOrderCommand, Unit>
{
    public async Task<Unit> Handle(SubmitSalesOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetAsync(new SalesOrderId(cmd.SalesOrderId), ct)
            ?? throw new NotFoundException($"Sales order {cmd.SalesOrderId} not found.");

        // DOMAIN decides IF submitting is legal (draft? non-empty? approved if needed?). Throws if not.
        order.Submit(cmd.ApprovalGranted);

        await using var tx = await uow.BeginAsync(ct);

        // INVENTORY (across the wall) makes each reservation safe under concurrency.
        foreach (var line in order.Lines)
        {
            var reserved = await stock.TryReserveAsync(
                line.ProductId.Value, line.Quantity.Value, tx.Inner, ct);
            if (!reserved)
                throw new DomainException($"Insufficient stock for product {line.ProductId.Value}.");
        }

        await orders.UpdateAsync(order, tx.Inner, ct);   // save Submitted status + version check
        await tx.CommitAsync(ct);

        return Unit.Value;
    }
}
