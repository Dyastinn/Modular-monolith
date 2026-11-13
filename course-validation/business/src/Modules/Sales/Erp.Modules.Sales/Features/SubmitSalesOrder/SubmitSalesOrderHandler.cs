using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Erp.Modules.Sales.Features;

internal sealed record SubmitSalesOrderCommand(long OrderId);

internal sealed class SubmitSalesOrderHandler(SalesDbContext db, IStockReservation stock, ICurrentUser currentUser, TimeProvider time)
{
    public async Task<bool> HandleAsync(SubmitSalesOrderCommand cmd, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var order = await db.GetVisibleOrderAsync(cmd.OrderId, new UserId(currentUser.UserId), currentUser.Role, ct);
        if (order is null) return false;
        await db.LockCustomerAsync(order.CustomerId, ct);
        var customer = await db.Customers.SingleAsync(c => c.Id == order.CustomerId, ct);
        var otherPending = await db.PendingTotalAsync(order.CustomerId, order.Id, ct);

        order.Submit(customer, otherPending, time.GetUtcNow().UtcDateTime);   // may approve it on the spot

        foreach (var line in order.Lines)
        {
            if (!await stock.TryReserveAsync(line.ItemId.Value, line.Quantity.Value, tx.GetDbTransaction(), ct))
                throw new DomainException(SalesErrors.InsufficientStock, $"Insufficient stock for {line.ItemName}. Submission cancelled.");
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
