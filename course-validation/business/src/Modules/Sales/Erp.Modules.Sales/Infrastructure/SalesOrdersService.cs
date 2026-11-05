using System.Data.Common;
using Erp.Modules.Sales.Contracts;
using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Sales.Infrastructure;

// Sales' service window for the modules that ship and bill its orders.
internal sealed class SalesOrdersService(SalesDbContext db) : ISalesOrders
{
    public Task<ShippableOrder> GetForShipmentAsync(long orderId, DbTransaction tx, CancellationToken ct) =>
        InTransaction(tx, async () =>
        {
            var id = new SalesOrderId(orderId);
            await db.LockOrderAsync(id, ct);   // two clerks shipping the same order: the second waits, then sees Shipped
            var order = await db.SalesOrders.SingleOrDefaultAsync(o => o.Id == id, ct)
                ?? throw new NotFoundException($"Sales order {orderId} not found.");
            order.EnsureCanShip();
            return new ShippableOrder(order.Id.Value, order.OrderNumber,
                order.Lines.Select(l => new ShippableLine(l.ItemId.Value, l.Quantity.Value)).ToList());
        }, ct);

    public Task MarkShippedAsync(long orderId, DateTime shippedUtc, DbTransaction tx, CancellationToken ct) =>
        InTransaction(tx, async () =>
        {
            var id = new SalesOrderId(orderId);
            var order = await db.SalesOrders.SingleAsync(o => o.Id == id, ct);
            order.MarkShipped(shippedUtc);
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);

    public async Task<OrderForInvoice> GetForInvoiceAsync(long orderId, CancellationToken ct)
    {
        var id = new SalesOrderId(orderId);
        var order = await db.SalesOrders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new NotFoundException($"Sales order {orderId} not found.");
        var customer = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == order.CustomerId, ct);

        return new OrderForInvoice(order.Id.Value, order.OrderNumber, customer.Id.Value, customer.Name, customer.Tin,
            customer.Address, customer.PaymentTermsDays,
            order.Lines.Select(l => new InvoiceableLine(l.ItemId.Value, l.ItemName, l.Unit, l.Quantity.Value, l.UnitPrice.Amount)).ToList());
    }

    // Join the caller's transaction for one operation, then let go: the caller owns the commit.
    private async Task<T> InTransaction<T>(DbTransaction tx, Func<Task<T>> work, CancellationToken ct)
    {
        await db.Database.UseTransactionAsync(tx, ct);
        try { return await work(); }
        finally { await db.Database.UseTransactionAsync(null, ct); }
    }
}
