using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Erp.Shared.BuildingBlocks;

namespace Erp.Integration.Tests;

// Edge cases from the lessons that grow the product: users, the item catalog, shipping.
[Collection("Integration")]
public class LaterModuleEdgeCaseTests(CustomWebAppFactory factory)
{
    private sealed record Created(long Id);
    private sealed record Me(long Id);

    // Lesson 16: disabling someone must also end the sessions they already have.
    [Fact]
    public async Task A_disabled_user_cannot_refresh_their_session()
    {
        var email = await factory.CreateUserAsync(Roles.SalesRep);
        var client = factory.CreateClient();
        var tokens = await factory.LoginAsync(client, email);
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
        var me = await client.GetFromJsonAsync<Me>("/api/auth/me");

        var admin = await factory.ClientFor("admin@erp.local");
        (await admin.PostAsync($"/api/users/{me!.Id}/disable", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { tokens.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Lesson 17: two people adding the same SKU at once; the unique index catches what the check missed.
    [Fact]
    public async Task The_same_sku_twice_is_409()
    {
        var admin = await factory.ClientFor("admin@erp.local");
        var item = new { sku = $"DUP-{Guid.NewGuid():N}"[..20], name = "Duplicate", unit = "pc", allowsFractions = false };

        var responses = await Task.WhenAll(admin.PostAsJsonAsync("/api/items", item), admin.PostAsJsonAsync("/api/items", item));

        responses.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.Created, HttpStatusCode.Conflict]);
    }

    // Lesson 18: only an approved order leaves the warehouse.
    [Fact]
    public async Task A_draft_order_cannot_be_shipped()
    {
        var rep = await factory.ClientFor("rep@erp.local");
        var create = await rep.PostAsJsonAsync("/api/sales-orders", new { customerId = 5, lines = new[] { new { itemId = 1, quantity = 1 } } });
        var id = (await create.Content.ReadFromJsonAsync<Created>())!.Id;

        var clerk = await factory.ClientFor("clerk@erp.local");
        (await clerk.PostAsJsonAsync("/api/shipments", new { salesOrderId = id })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
