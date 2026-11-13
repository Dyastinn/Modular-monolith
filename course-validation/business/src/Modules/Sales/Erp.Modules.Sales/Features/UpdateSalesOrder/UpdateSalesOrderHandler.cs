using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Sales.Features;

internal sealed record UpdateSalesOrderCommand(long OrderId, IReadOnlyList<OrderLineInput> Lines);

internal sealed class UpdateSalesOrderHandler(SalesDbContext db, ICurrentUser currentUser)
{
    public async Task<bool> HandleAsync(UpdateSalesOrderCommand cmd, CancellationToken ct)
    {
        var order = await db.GetVisibleOrderAsync(cmd.OrderId, new UserId(currentUser.UserId), currentUser.Role, ct);
        if (order is null) return false;
        var customer = await db.Customers.SingleAsync(c => c.Id == order.CustomerId, ct);
        customer.EnsureNotBlocked();

        order.SetLines(await db.ResolveLinesAsync(cmd.Lines.Select(l => (l.ItemId, l.Quantity)), ct), currentUser.Role);
        customer.EnsureWithinCreditLimit(order.Total, await db.PendingTotalAsync(customer.Id, order.Id, ct));

        await db.SaveChangesAsync(ct);
        return true;
    }
}
