using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Erp.Integration.Tests;

// End-to-end through the real HTTP + SQL pipeline (Phase 12 §5).
[Collection("Integration")]
public class SalesOrderWorkflowTests
{
    private readonly CustomWebAppFactory _factory;
    public SalesOrderWorkflowTests(CustomWebAppFactory factory) => _factory = factory;

    private sealed record LoginResponse(string Token, string Username, string Role);
    private sealed record CreatedResponse(long Id);
    private sealed record DetailResponse(string Status);

    private async Task<HttpClient> LoggedInAs(string username)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/auth/login", new { username, password = "Password123!" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Create_submit_approve_happy_path()
    {
        var client = await LoggedInAs("manager");

        var create = await client.PostAsJsonAsync("/sales-orders",
            new { customerId = 1, lines = new[] { new { itemId = 1, quantity = 2 } } });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await create.Content.ReadFromJsonAsync<CreatedResponse>())!.Id;

        (await client.PostAsync($"/sales-orders/{id}/submit", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PostAsync($"/sales-orders/{id}/approve", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var dto = await client.GetFromJsonAsync<DetailResponse>($"/sales-orders/{id}");
        dto!.Status.Should().Be("Approved");
    }

    [Fact]
    public async Task SalesRep_cannot_approve()
    {
        var client = await LoggedInAs("rep");

        var create = await client.PostAsJsonAsync("/sales-orders",
            new { customerId = 1, lines = new[] { new { itemId = 1, quantity = 1 } } });
        var id = (await create.Content.ReadFromJsonAsync<CreatedResponse>())!.Id;
        await client.PostAsync($"/sales-orders/{id}/submit", null);

        var approve = await client.PostAsync($"/sales-orders/{id}/approve", null);
        approve.StatusCode.Should().Be(HttpStatusCode.Forbidden);   // 403 — role gate works end-to-end
    }

    [Fact]
    public async Task Blocked_customer_is_rejected_with_422()
    {
        var client = await LoggedInAs("manager");
        var create = await client.PostAsJsonAsync("/sales-orders",
            new { customerId = 3, lines = new[] { new { itemId = 1, quantity = 1 } } });   // Gamma is blocked
        create.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Over_credit_limit_is_rejected_with_422()
    {
        var client = await LoggedInAs("manager");
        var create = await client.PostAsJsonAsync("/sales-orders",
            new { customerId = 2, lines = new[] { new { itemId = 1, quantity = 100 } } });   // Beta: $2,500 > $500 left
        create.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
