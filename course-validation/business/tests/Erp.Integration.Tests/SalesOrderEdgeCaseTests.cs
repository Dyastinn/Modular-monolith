using System.Globalization;
using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using AwesomeAssertions;
using Dapper;
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Integration.Tests;

// The edges of the order's life: wrong input, wrong person, wrong moment, and two people at once.
// Each test makes its own customer, so earlier tests' balances can't change the answer.
[Collection("Integration")]
public class SalesOrderEdgeCaseTests(CustomWebAppFactory factory)
{
    private const string Rep = "rep@erp.local", Manager = "manager@erp.local", Admin = "admin@erp.local";

    private sealed record Created(long Id);
    private sealed record Detail(string Status, string OrderNumber);
    private sealed record Summary(long Id, string OrderNumber, string CustomerName, decimal Total);
    private sealed record Page(int TotalCount, List<Summary> Items);
    private sealed record Problem(string Title, string? Detail);

    // Straight into the database: the customers screen and its API only arrive in lesson 15.
    private async Task<long> NewCustomer(string? name = null, decimal creditLimit = 1_000_000)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        var customer = Customer.Create(name ?? $"Edge {Guid.NewGuid():N}", null, null, new Money(creditLimit), 30);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id.Value;
    }

    private static Task<HttpResponseMessage> PostOrder(HttpClient client, long customerId, long itemId, decimal quantity) =>
        client.PostAsJsonAsync("/api/sales-orders", new { customerId, lines = new[] { new { itemId, quantity } } });

    private static async Task<long> CreateOrder(HttpClient client, long customerId, long itemId, decimal quantity)
    {
        var response = await PostOrder(client, customerId, itemId, quantity);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private async Task<decimal> Balance(long customerId)
    {
        await using var conn = factory.OpenDatabase();
        return await conn.ExecuteScalarAsync<decimal>("SELECT outstanding_balance FROM sales.customer WHERE id = @customerId", new { customerId });
    }

    private async Task<decimal> Reserved(long itemId)
    {
        await using var conn = factory.OpenDatabase();
        return await conn.ExecuteScalarAsync<decimal>("SELECT reserved FROM inventory.item WHERE id = @itemId", new { itemId });
    }

    // ---- Lesson 07: creating an order ----

    // 404 means the address (POST /api/sales-orders) doesn't exist. It does: what's wrong is an id inside the body,
    // so the request is understood but can't be processed: 422, with a code that says which id.
    [Fact]
    public async Task An_unknown_customer_or_item_is_422_with_its_code()
    {
        var rep = await factory.ClientFor(Rep);

        var noCustomer = await PostOrder(rep, customerId: 999_999, itemId: 1, quantity: 1);
        noCustomer.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await noCustomer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("SALES-0018");

        var noItem = await PostOrder(rep, await NewCustomer(), itemId: 999_999, quantity: 1);
        noItem.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await noItem.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("SALES-0019");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("1.2345")]
    public async Task A_quantity_that_is_zero_negative_or_too_precise_is_400(string quantity)
    {
        var rep = await factory.ClientFor(Rep);
        (await PostOrder(rep, await NewCustomer(), 1, decimal.Parse(quantity, CultureInfo.InvariantCulture))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // Shape is fine (400 rules pass), but widgets are counted in whole pieces: that's the item's rule, so 422.
    [Fact]
    public async Task Half_a_widget_is_a_business_rule_not_a_bad_request()
    {
        var rep = await factory.ClientFor(Rep);
        (await PostOrder(rep, await NewCustomer(), itemId: 1, quantity: 1.5m)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Broken_json_is_400_with_problem_details_not_500()
    {
        var rep = await factory.ClientFor(Rep);
        var response = await rep.PostAsync("/api/sales-orders", new StringContent("{ \"customerId\": ", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<Problem>())!.Title.Should().Be("The request could not be read");
    }

    [Fact]
    public async Task Orders_created_at_the_same_moment_get_different_numbers()
    {
        var rep = await factory.ClientFor(Rep);
        var customerId = await NewCustomer();

        var ids = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => CreateOrder(rep, customerId, 1, 1)));
        var numbers = await Task.WhenAll(ids.Select(async id => (await rep.GetFromJsonAsync<Detail>($"/api/sales-orders/{id}"))!.OrderNumber));

        numbers.Should().OnlyHaveUniqueItems();
    }

    // ---- Lesson 08: finding orders ----

    // ILIKE treats % and _ as wildcards. A search for "50%" must look for those characters, not match everything.
    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("\\")]
    public async Task Wildcard_characters_in_a_search_are_matched_literally(string search)
    {
        var rep = await factory.ClientFor(Rep);
        await CreateOrder(rep, await NewCustomer(), 1, 1);

        var page = await rep.GetFromJsonAsync<Page>($"/api/sales-orders?search={Uri.EscapeDataString(search)}");

        page!.TotalCount.Should().Be(0, "no order number or customer name contains '{0}'", search);
    }

    [Fact]
    public async Task A_search_finds_names_with_quotes_and_ignores_case()
    {
        var rep = await factory.ClientFor(Rep);
        var name = $"O'Brien & Sons {Guid.NewGuid():N}";
        await CreateOrder(rep, await NewCustomer(name), 1, 1);

        var page = await rep.GetFromJsonAsync<Page>($"/api/sales-orders?search={Uri.EscapeDataString(name.ToUpperInvariant())}");

        page!.Items.Should().ContainSingle().Which.CustomerName.Should().Be(name);
    }

    // The total lives twice: in C# (the domain, for credit) and in SQL (the list). They must never disagree.
    [Fact]
    public async Task The_list_shows_the_same_total_the_domain_computes()
    {
        var rep = await factory.ClientFor(Rep);
        var name = $"Totals {Guid.NewGuid():N}";
        var id = await CreateOrder(rep, await NewCustomer(name), itemId: 5, quantity: 1.333m);   // 620.50 × 1.333 = 827.1265

        var listed = (await rep.GetFromJsonAsync<Page>($"/api/sales-orders?search={name}"))!.Items.Single();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        var order = await db.SalesOrders.FindAsync(new SalesOrderId(id));
        listed.Total.Should().Be(order!.Total.Amount).And.Be(827.13m);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("status=99")]
    [InlineData("status=Lost")]
    public async Task A_bad_page_or_status_is_400(string query) =>
        (await (await factory.ClientFor(Rep)).GetAsync($"/api/sales-orders?{query}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    // A rep can't even learn that another rep's order exists: not 403, but 404.
    [Fact]
    public async Task Another_reps_order_is_404_for_reading_submitting_and_editing()
    {
        var mine = await factory.ClientFor(Rep);
        var other = await factory.ClientFor(await factory.CreateUserAsync(Roles.SalesRep));
        var id = await CreateOrder(mine, await NewCustomer(), 1, 1);

        (await other.GetAsync($"/api/sales-orders/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PostAsync($"/api/sales-orders/{id}/submit", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PutAsJsonAsync($"/api/sales-orders/{id}", new { lines = new[] { new { itemId = 1, quantity = 2 } } }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Lesson 09: submitting ----

    [Fact]
    public async Task Submitting_twice_is_refused_and_charges_once()
    {
        var rep = await factory.ClientFor(Rep);
        var customerId = await NewCustomer();
        var id = await CreateOrder(rep, customerId, 1, 2);

        (await rep.PostAsync($"/api/sales-orders/{id}/submit", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await rep.PostAsync($"/api/sales-orders/{id}/submit", null)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await Balance(customerId)).Should().Be(50);
    }

    // Approval and the charge happen before the stock check fails; the transaction must take all of it back.
    [Fact]
    public async Task Too_little_stock_refuses_the_submit_and_leaves_everything_as_it_was()
    {
        const long kit = 3;   // dev seed: 8 on hand
        await using var conn = factory.OpenDatabase();
        var reservedBefore = await Reserved(kit);
        await conn.ExecuteAsync("UPDATE inventory.item SET reserved = 6 WHERE id = @kit", new { kit });
        try
        {
            var manager = await factory.ClientFor(Manager);
            var customerId = await NewCustomer();
            var id = await CreateOrder(manager, customerId, kit, 3);   // 3 wanted, 2 available

            var submit = await manager.PostAsync($"/api/sales-orders/{id}/submit", null);

            submit.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await manager.GetFromJsonAsync<Detail>($"/api/sales-orders/{id}"))!.Status.Should().Be("Draft");
            (await Reserved(kit)).Should().Be(6);
            (await Balance(customerId)).Should().Be(0);
            (await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM sales.outbox_message WHERE (payload->'Id'->>'Value')::bigint = @id", new { id }))
                .Should().Be(0, "the approval event was rolled back with everything else");
        }
        finally
        {
            await conn.ExecuteAsync("UPDATE inventory.item SET reserved = @reservedBefore WHERE id = @kit", new { reservedBefore, kit });
        }
    }

    // ---- Lesson 10: approve, reject, cancel ----

    [Fact]
    public async Task Two_managers_approving_at_once_charge_the_customer_once()
    {
        var rep = await factory.ClientFor(Rep);
        var customerId = await NewCustomer();
        var id = await CreateOrder(rep, customerId, 4, 1);   // a ₱75,000 rack: waits for a manager
        await rep.PostAsync($"/api/sales-orders/{id}/submit", null);

        var manager = await factory.ClientFor(Manager);
        var admin = await factory.ClientFor(Admin);
        var responses = await Task.WhenAll(
            manager.PostAsync($"/api/sales-orders/{id}/approve", null),
            admin.PostAsync($"/api/sales-orders/{id}/approve", null));

        responses.Count(r => r.StatusCode == HttpStatusCode.NoContent).Should().Be(1);
        responses.Select(r => r.StatusCode).Should().Contain(s => s == HttpStatusCode.Conflict || s == HttpStatusCode.UnprocessableEntity);
        (await Balance(customerId)).Should().Be(75_000);
    }

    [Fact]
    public async Task A_step_out_of_order_is_422()
    {
        var rep = await factory.ClientFor(Rep);
        var manager = await factory.ClientFor(Manager);
        var id = await CreateOrder(rep, await NewCustomer(), 1, 1);

        (await manager.PostAsync($"/api/sales-orders/{id}/approve", null)).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity, "a draft hasn't been submitted");
        (await rep.PostAsync($"/api/sales-orders/{id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await rep.PostAsync($"/api/sales-orders/{id}/cancel", null)).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity, "a cancelled order stays cancelled");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_rejection_without_a_reason_is_refused(string reason)
    {
        var rep = await factory.ClientFor(Rep);
        var manager = await factory.ClientFor(Manager);
        var id = await CreateOrder(rep, await NewCustomer(), 4, 1);
        await rep.PostAsync($"/api/sales-orders/{id}/submit", null);

        var response = await manager.PostAsJsonAsync($"/api/sales-orders/{id}/reject", new { reason });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity);
        (await rep.GetFromJsonAsync<Detail>($"/api/sales-orders/{id}"))!.Status.Should().Be("PendingApproval");
    }

    // ---- Lesson 11: editing an order ----

    [Fact]
    public async Task A_submitted_order_cannot_be_edited()
    {
        var rep = await factory.ClientFor(Rep);
        var id = await CreateOrder(rep, await NewCustomer(), 1, 1);
        await rep.PostAsync($"/api/sales-orders/{id}/submit", null);

        (await rep.PutAsJsonAsync($"/api/sales-orders/{id}", new { lines = new[] { new { itemId = 1, quantity = 5 } } }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Removing_every_line_is_400_and_keeps_the_old_lines()
    {
        var rep = await factory.ClientFor(Rep);
        var id = await CreateOrder(rep, await NewCustomer(), 1, 3);

        (await rep.PutAsJsonAsync($"/api/sales-orders/{id}", new { lines = Array.Empty<object>() }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var conn = factory.OpenDatabase();
        (await conn.ExecuteScalarAsync<decimal>("SELECT SUM(quantity) FROM sales.sales_order_line WHERE sales_order_id = @id", new { id }))
            .Should().Be(3);
    }
}
