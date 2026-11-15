using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Erp.Modules.Sales.Features;

internal sealed record CloseSalesOrderCommand(long OrderId);

// A partly shipped order whose rest will never ship: stock back to available, charge back off the balance.
internal sealed class CloseSalesOrderHandler(SalesDbContext db, IStockReservation stock, ICurrentUser currentUser)
{
    public async Task<bool> HandleAsync(CloseSalesOrderCommand cmd, CancellationToken ct)
    {
        var order = await db.GetVisibleOrderAsync(cmd.OrderId, new UserId(currentUser.UserId), currentUser.Role, ct);
        if (order is null) return false;
        var customer = await db.Customers.SingleAsync(c => c.Id == order.CustomerId, ct);

        var toRelease = order.CloseShort(customer);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var (itemId, quantity) in toRelease)
            await stock.ReleaseAsync(itemId.Value, quantity, tx.GetDbTransaction(), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
