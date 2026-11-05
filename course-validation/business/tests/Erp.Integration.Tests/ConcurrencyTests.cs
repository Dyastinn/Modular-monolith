using AwesomeAssertions;
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Integration.Tests;

// Lost updates: two people load the same draft, both edit it, both save. The second save must fail rather
// than silently overwrite the first person's lines.
[Collection("Integration")]
public class ConcurrencyTests(CustomWebAppFactory factory)
{
    [Fact]
    public async Task Second_of_two_concurrent_line_edits_is_rejected()
    {
        var orderId = await CreateDraft();

        await using var alice = factory.Services.CreateAsyncScope();
        await using var bob = factory.Services.CreateAsyncScope();
        var aliceDb = alice.ServiceProvider.GetRequiredService<SalesDbContext>();
        var bobDb = bob.ServiceProvider.GetRequiredService<SalesDbContext>();

        // Both load the same version of the order.
        var aliceOrder = await aliceDb.SalesOrders.SingleAsync(o => o.Id == orderId);
        var bobOrder = await bobDb.SalesOrders.SingleAsync(o => o.Id == orderId);

        // Each replaces the lines — a change that writes ONLY to sales_order_line.
        aliceOrder.ClearLines();
        aliceOrder.AddLine(new ItemId(1), new Quantity(3), new Money(25));
        bobOrder.ClearLines();
        bobOrder.AddLine(new ItemId(2), new Quantity(1), new Money(40));

        await aliceDb.SaveChangesAsync();   // first one wins

        var bobSaves = () => bobDb.SaveChangesAsync();
        await bobSaves.Should().ThrowAsync<DbUpdateConcurrencyException>();   // → HTTP 409 via GlobalExceptionHandler
    }

    private async Task<SalesOrderId> CreateDraft()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        var now = DateTime.UtcNow;
        var order = SalesOrder.Create(await db.NextOrderNumberAsync(now, default), new CustomerId(1), new UserId(1), now);
        order.AddLine(new ItemId(1), new Quantity(1), new Money(25));
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }
}
