using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Sales.Features;

internal sealed record ApproveSalesOrderCommand(long OrderId) : ICommand;

internal sealed class ApproveSalesOrderHandler(SalesDbContext db, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<ApproveSalesOrderCommand>
{
    public async ValueTask<Unit> Handle(ApproveSalesOrderCommand cmd, CancellationToken ct)
    {
        var approver = new UserId(currentUser.UserId);
        var order = await db.GetVisibleOrderAsync(cmd.OrderId, approver, currentUser.Role, ct);
        var customer = await db.Customers.SingleAsync(c => c.Id == order.CustomerId, ct);

        order.Approve(approver, customer, clock.UtcNow);

        await db.SaveChangesAsync(ct);   // order, customer balance and outbox event: one transaction
        return Unit.Value;
    }
}
