using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Erp.Modules.Sales.Features;

internal sealed record CancelSalesOrderCommand(long OrderId) : ICommand;

internal sealed class CancelSalesOrderHandler(SalesDbContext db, IStockReservation stock, ICurrentUser currentUser)
    : ICommandHandler<CancelSalesOrderCommand>
{
    public async ValueTask<Unit> Handle(CancelSalesOrderCommand cmd, CancellationToken ct)
    {
        var order = await db.GetVisibleOrderAsync(cmd.OrderId, new UserId(currentUser.UserId), currentUser.Role, ct);
        var customer = await db.Customers.SingleAsync(c => c.Id == order.CustomerId, ct);

        var toRelease = order.Cancel(customer);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var line in toRelease)
            await stock.ReleaseAsync(line.ItemId.Value, line.Quantity.Value, tx.GetDbTransaction(), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Unit.Value;
    }
}
