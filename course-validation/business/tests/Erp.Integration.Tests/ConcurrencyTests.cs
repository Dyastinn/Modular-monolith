using AwesomeAssertions;
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Integration.Tests;

[Collection("Integration")]
public class ConcurrencyTests(CustomWebAppFactory factory)
{
    // Two people load the same draft and both replace its lines: the second save must fail, not overwrite.
    [Fact]
    public async Task Second_of_two_concurrent_line_edits_is_rejected()
    {
        var orderId = await CreateDraft();

        await using var alice = factory.Services.CreateAsyncScope();
        await using var bob = factory.Services.CreateAsyncScope();
        var aliceDb = alice.ServiceProvider.GetRequiredService<SalesDbContext>();
        var bobDb = bob.ServiceProvider.GetRequiredService<SalesDbContext>();
        var aliceOrder = await aliceDb.SalesOrders.SingleAsync(o => o.Id == orderId);
        var bobOrder = await bobDb.SalesOrders.SingleAsync(o => o.Id == orderId);

        aliceOrder.SetLines(await aliceDb.ResolveLinesAsync([(1, 3)], default), Roles.SalesRep);
        bobOrder.SetLines(await bobDb.ResolveLinesAsync([(2, 1)], default), Roles.SalesRep);

        await aliceDb.SaveChangesAsync();
        var bobSaves = () => bobDb.SaveChangesAsync();
        await bobSaves.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    private async Task<SalesOrderId> CreateDraft()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        var now = DateTime.UtcNow;
        var order = SalesOrder.Create(await db.NextOrderNumberAsync(now, default), new CustomerId(1), new UserId(1), now);
        order.SetLines(await db.ResolveLinesAsync([(1, 1)], default), Roles.SalesRep);
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }
}
