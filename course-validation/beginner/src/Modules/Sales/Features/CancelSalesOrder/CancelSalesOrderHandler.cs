using Erp.Modules.Sales.Domain;
using Erp.Shared.Common;
using Erp.Shared.Common.Contracts;
using Erp.Shared.Infrastructure.Persistence;
using Erp.Shared.Messaging;
using Microsoft.Extensions.Logging;

namespace Erp.Modules.Sales.Features.CancelSalesOrder;

public sealed record CancelSalesOrderCommand(long SalesOrderId) : ICommand<Unit>;

public sealed class CancelSalesOrderHandler(
    ISalesOrderRepository orders, IStockReservation stock, IUnitOfWork uow, IPublisher publisher)
    : ICommandHandler<CancelSalesOrderCommand, Unit>
{
    public async Task<Unit> Handle(CancelSalesOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetAsync(new SalesOrderId(cmd.SalesOrderId), ct)
            ?? throw new NotFoundException($"Sales order {cmd.SalesOrderId} not found.");

        order.Cancel();

        await using var tx = await uow.BeginAsync(ct);
        foreach (var line in order.Lines)
            await stock.ReleaseAsync(line.ProductId.Value, line.Quantity.Value, tx.Inner, ct);
        await orders.UpdateAsync(order, tx.Inner, ct);
        await tx.CommitAsync(ct);

        // Dispatch domain events AFTER commit (events implement both IDomainEvent and INotification).
        foreach (var e in order.DomainEvents) await publisher.Publish((INotification)e, ct);
        order.ClearEvents();

        return Unit.Value;
    }
}

// A side-effect reaction kept OUT of the domain — proves the notification path works.
public sealed class SalesOrderCancelledAuditHandler(ILogger<SalesOrderCancelledAuditHandler> log)
    : INotificationHandler<SalesOrderCancelled>
{
    public Task Handle(SalesOrderCancelled e, CancellationToken ct)
    {
        log.LogInformation("AUDIT: sales order {Id} was cancelled.", e.Id.Value);
        return Task.CompletedTask;
    }
}
