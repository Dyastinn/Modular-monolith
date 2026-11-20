using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;

namespace Yasda.Integration.Tests;

// Every error a person can see carries a stable code, and the code's link opens its entry in the catalog.
[Collection("Integration")]
public class ErrorCodeTests(CustomWebAppFactory factory)
{
    private const string Admin = "admin@yasda.local";

    [Fact]
    public async Task A_refused_business_rule_answers_with_its_code_its_link_and_the_trace_id()
    {
        var admin = await factory.ClientFor(Admin);
        var me = await admin.GetFromJsonAsync<JsonElement>("/api/auth/me");

        var response = await admin.PostAsync($"/api/users/{me.GetProperty("id").GetInt64()}/disable", null);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("IDENTITY-0003");
        problem.GetProperty("type").GetString().Should().Be("/errors/IDENTITY-0003");
        problem.GetProperty("title").GetString().Should().Be("You can't disable your own account");
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    // The framework writes these responses itself; they get a code from their status.
    [Theory]
    [InlineData("/api/auth/me", false, "YASDA-0005")]          // not signed in
    [InlineData("/api/no-such-thing", true, "YASDA-0002")]     // unknown address
    [InlineData("/api/users/999999", true, "YASDA-0002")]      // unknown user
    public async Task Errors_the_framework_writes_carry_a_code_too(string path, bool signedIn, string code)
    {
        var client = signedIn ? await factory.ClientFor(Admin) : factory.CreateClient();

        var problem = await (await client.GetAsync(path)).Content.ReadFromJsonAsync<JsonElement>();

        problem.GetProperty("code").GetString().Should().Be(code);
        problem.GetProperty("type").GetString().Should().Be($"/errors/{code}");
    }

    [Fact]
    public async Task The_catalog_explains_a_code_to_anyone_even_signed_out()
    {
        var error = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/errors/identity-0003");

        error.GetProperty("code").GetString().Should().Be("IDENTITY-0003");
        error.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
        error.GetProperty("fix").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task The_catalog_lists_every_module_and_an_unknown_code_is_404()
    {
        var client = factory.CreateClient();

        var all = await client.GetFromJsonAsync<JsonElement[]>("/api/errors");
        // "TICKETS-0001" → "TICKETS": the module each code belongs to.
        var codes = all!.Select(e => e.GetProperty("code").GetString()!).ToList();
        var prefixes = codes.Select(c => c.Split('-')[0]).Distinct();
        prefixes.Should().BeEquivalentTo(["YASDA", "IDENTITY", "TICKETS", "SLA"]);

        (await client.GetAsync("/api/errors/IDENTITY-9999")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
