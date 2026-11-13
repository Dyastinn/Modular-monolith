using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Dapper;
using Npgsql;

namespace Erp.Integration.Tests;

// Seed: customers 1 Acme, 2 Beta (₱500 left), 3 Gamma (blocked), 4 Delta (₱200k limit), 5 Epsilon (large);
// items 1 Widget ₱25, 2 Gadget ₱40, 3 Restricted Kit ₱500, 4 Server Rack ₱75,000, 5 Copper Wire ₱620.50/kg.
[Collection("Integration")]
public class SalesOrderWorkflowTests(CustomWebAppFactory factory)
{
    private const string Rep = "rep@erp.local", Manager = "manager@erp.local";

    private sealed record Created(long Id);
    private sealed record Detail(string Status, string? RejectionReason);
    private sealed record Page(int TotalCount, List<object> Items);

    private static async Task<long> CreateOrder(HttpClient client, long customerId, long itemId, int quantity)
    {
        var response = await client.PostAsJsonAsync("/api/sales-orders",
            new { customerId, lines = new[] { new { itemId, quantity } } });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private static async Task<string> StatusOf(HttpClient client, long id) =>
        (await client.GetFromJsonAsync<Detail>($"/api/sales-orders/{id}"))!.Status;

    [Fact]
    public async Task Small_order_is_approved_on_submit()
    {
        var rep = await factory.ClientFor(Rep);
        var id = await CreateOrder(rep, customerId: 1, itemId: 1, quantity: 2);

        (await rep.PostAsync($"/api/sales-orders/{id}/submit", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await StatusOf(rep, id)).Should().Be("Approved");
    }

    [Fact]
    public async Task High_value_order_waits_for_a_manager_who_did_not_create_it()
    {
        var rep = await factory.ClientFor(Rep);
        var manager = await factory.ClientFor(Manager);
        var id = await CreateOrder(rep, customerId: 5, itemId: 4, quantity: 1);
        await rep.PostAsync($"/api/sales-orders/{id}/submit", null);
        (await StatusOf(rep, id)).Should().Be("PendingApproval");

        (await manager.PostAsync($"/api/sales-orders/{id}/approve", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await StatusOf(rep, id)).Should().Be("Approved");
    }

    [Fact]
    public async Task Creator_cannot_approve_their_own_order()
    {
        var manager = await factory.ClientFor(Manager);
        var id = await CreateOrder(manager, customerId: 5, itemId: 4, quantity: 1);
        await manager.PostAsync($"/api/sales-orders/{id}/submit", null);

        (await manager.PostAsync($"/api/sales-orders/{id}/approve", null)).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task SalesRep_cannot_approve()
    {
        var rep = await factory.ClientFor(Rep);
        var id = await CreateOrder(rep, customerId: 5, itemId: 4, quantity: 1);
        await rep.PostAsync($"/api/sales-orders/{id}/submit", null);

        (await rep.PostAsync($"/api/sales-orders/{id}/approve", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Rejecting_records_the_reason_and_releases_the_stock()
    {
        var rep = await factory.ClientFor(Rep);
        var manager = await factory.ClientFor(Manager);
        var before = await ReservedRacks();
        var id = await CreateOrder(rep, customerId: 5, itemId: 4, quantity: 2);
        await rep.PostAsync($"/api/sales-orders/{id}/submit", null);
        (await ReservedRacks()).Should().Be(before + 2);

        var reject = await manager.PostAsJsonAsync($"/api/sales-orders/{id}/reject", new { reason = "Price not agreed" });

        reject.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var detail = await rep.GetFromJsonAsync<Detail>($"/api/sales-orders/{id}");
        detail!.Status.Should().Be("Rejected");
        detail.RejectionReason.Should().Be("Price not agreed");
        (await ReservedRacks()).Should().Be(before);
    }

    [Fact]
    public async Task Orders_awaiting_approval_count_against_credit()
    {
        var rep = await factory.ClientFor(Rep);
        var first = await CreateOrder(rep, customerId: 4, itemId: 4, quantity: 2);   // ₱150,000 of ₱200,000
        await rep.PostAsync($"/api/sales-orders/{first}/submit", null);

        var second = await rep.PostAsJsonAsync("/api/sales-orders",
            new { customerId = 4, lines = new[] { new { itemId = 4, quantity = 1 } } });   // +₱75,000

        second.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Blocked_customer_is_rejected_with_422()
    {
        var manager = await factory.ClientFor(Manager);
        var create = await manager.PostAsJsonAsync("/api/sales-orders",
            new { customerId = 3, lines = new[] { new { itemId = 1, quantity = 1 } } });
        create.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Over_credit_limit_is_rejected_with_422()
    {
        var manager = await factory.ClientFor(Manager);
        var create = await manager.PostAsJsonAsync("/api/sales-orders",
            new { customerId = 2, lines = new[] { new { itemId = 1, quantity = 100 } } });   // ₱2,500 > ₱500 left
        create.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // Regression guard: `page`/`pageSize` must be `int?`, or a bare GET fails model binding with 400.
    [Fact]
    public async Task Search_with_no_query_string_returns_the_first_page()
    {
        var rep = await factory.ClientFor(Rep);
        (await rep.GetAsync("/api/sales-orders")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_page_past_the_end_still_reports_the_real_total()
    {
        var rep = await factory.ClientFor(Rep);
        await CreateOrder(rep, customerId: 1, itemId: 1, quantity: 1);

        var page = await rep.GetFromJsonAsync<Page>("/api/sales-orders?page=9999");

        page!.Items.Should().BeEmpty();
        page.TotalCount.Should().BeGreaterThan(0);
    }

    // Regression guard: validators are internal; without includeInternalTypes: true they silently never run.
    [Fact]
    public async Task Order_with_no_lines_is_rejected_by_the_internal_validator()
    {
        var manager = await factory.ClientFor(Manager);
        var create = await manager.PostAsJsonAsync("/api/sales-orders", new { customerId = 1, lines = Array.Empty<object>() });
        create.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        create.Content.Headers.ContentType!.ToString().Should().Be("application/problem+json; charset=utf-8");
        var problem = await create.Content.ReadFromJsonAsync<Problem>();
        problem!.Detail.Should().Be("An order needs at least one line.");
        problem.Code.Should().Be("ERP-0001");
        problem.Errors["Lines"].Should().Equal("An order needs at least one line.");   // keyed by field, so a form can mark it
    }

    private sealed record Problem(string Detail, string Code, Dictionary<string, string[]> Errors);

    private async Task<decimal> ReservedRacks()
    {
        await using var conn = factory.OpenDatabase();
        return await conn.ExecuteScalarAsync<decimal>("SELECT reserved FROM inventory.item WHERE id = 4");
    }
}
