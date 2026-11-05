using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Sales.Features;

internal sealed record ApproveSalesOrderCommand(long OrderId) : ICommand;

internal sealed class ApproveSalesOrderHandler(
    SalesDbContext db, ICurrentUser currentUser, IClock clock, IPublisher publisher)
    : ICommandHandler<ApproveSalesOrderCommand>
{
    public async ValueTask<Unit> Handle(ApproveSalesOrderCommand cmd, CancellationToken ct)
    {
        var orderId = new SalesOrderId(cmd.OrderId);
        var order = await db.SalesOrders.SingleOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new NotFoundException($"Sales order {cmd.OrderId} not found.");
        var customer = await db.Customers.SingleAsync(c => c.Id == order.CustomerId, ct);

        order.Approve(new UserId(currentUser.UserId), clock.UtcNow);   // domain: only a PendingApproval order
        customer.AddToBalance(order.Total);                            // the customer now owes this

        // Both rows in ONE SaveChanges, which EF wraps in a transaction: approved-and-charged, or neither.
        await db.SaveChangesAsync(ct);

        await publisher.PublishDomainEventsAsync(order, ct);   // after the commit: audit log etc.
        return Unit.Value;
    }
}
