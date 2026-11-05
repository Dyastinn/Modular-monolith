using Erp.Modules.Inventory.Contracts;   // Inventory's service window — never Inventory itself
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Erp.Modules.Sales.Features;

internal sealed record SubmitSalesOrderCommand(long OrderId) : ICommand;

internal sealed class SubmitSalesOrderHandler(SalesDbContext db, IStockReservation stock)
    : ICommandHandler<SubmitSalesOrderCommand>
{
    public async ValueTask<Unit> Handle(SubmitSalesOrderCommand cmd, CancellationToken ct)
    {
        var orderId = new SalesOrderId(cmd.OrderId);
        var order = await db.SalesOrders.SingleOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new NotFoundException($"Sales order {cmd.OrderId} not found.");

        // Re-check the customer: things may have changed since the draft was created.
        var customer = await db.Customers.SingleAsync(c => c.Id == order.CustomerId, ct);
        customer.EnsureNotBlocked();
        customer.EnsureWithinCreditLimit(order.Total);

        order.Submit();   // domain: must be a non-empty Draft

        // Reserving stock (Inventory's tables) and the status change (ours) must succeed or fail TOGETHER, so
        // both run inside one transaction that this handler owns and Inventory joins.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        foreach (var line in order.Lines)
        {
            var reserved = await stock.TryReserveAsync(line.ItemId.Value, line.Quantity.Value, tx.GetDbTransaction(), ct);
            if (!reserved)
                throw new DomainException($"Insufficient stock for item {line.ItemId.Value}. Submission cancelled.");
            // throwing here disposes tx without a commit → every reservation so far rolls back
        }

        await db.SaveChangesAsync(ct);   // version-checked status change
        await tx.CommitAsync(ct);
        return Unit.Value;
    }
}
