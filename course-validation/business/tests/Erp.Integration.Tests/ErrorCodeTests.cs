using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;

namespace Erp.Integration.Tests;

// Every error a person can see carries a stable code, and the code's link opens its entry in the catalog.
[Collection("Integration")]
public class ErrorCodeTests(CustomWebAppFactory factory)
{
    private const string Manager = "manager@erp.local";

    [Fact]
    public async Task A_refused_business_rule_answers_with_its_code_its_link_and_the_trace_id()
    {
        var manager = await factory.ClientFor(Manager);

        var response = await manager.PostAsJsonAsync("/api/sales-orders",
            new { customerId = 3, lines = new[] { new { itemId = 1, quantity = 1 } } });   // customer 3 is blocked

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("SALES-0002");
        problem.GetProperty("type").GetString().Should().Be("/errors/SALES-0002");
        problem.GetProperty("title").GetString().Should().Be("The customer is blocked");
        problem.GetProperty("detail").GetString().Should().Contain("blocked");   // the specifics stay in the detail
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    // The framework writes these responses itself; they get a code from their status.
    [Theory]
    [InlineData("/api/sales-orders", false, "ERP-0006")]          // not signed in
    [InlineData("/api/no-such-thing", true, "ERP-0002")]          // unknown address
    [InlineData("/api/sales-orders/999999", true, "ERP-0002")]    // unknown order
    public async Task Errors_the_framework_writes_carry_a_code_too(string path, bool signedIn, string code)
    {
        var client = signedIn ? await factory.ClientFor(Manager) : factory.CreateClient();

        var problem = await (await client.GetAsync(path)).Content.ReadFromJsonAsync<JsonElement>();

        problem.GetProperty("code").GetString().Should().Be(code);
        problem.GetProperty("type").GetString().Should().Be($"/errors/{code}");
    }

    [Fact]
    public async Task The_catalog_explains_a_code_to_anyone_even_signed_out()
    {
        var error = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/errors/sales-0003");

        error.GetProperty("code").GetString().Should().Be("SALES-0003");
        error.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
        error.GetProperty("fix").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task The_catalog_lists_every_module_and_an_unknown_code_is_404()
    {
        var client = factory.CreateClient();

        var all = await client.GetFromJsonAsync<JsonElement[]>("/api/errors");
        all!.Select(e => e.GetProperty("code").GetString()!.Split('-')[0]).Distinct()
            .Should().BeEquivalentTo(["ERP", "IDENTITY", "SALES", "INVENTORY", "PURCHASING", "INVOICING"]);

        (await client.GetAsync("/api/errors/SALES-9999")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
