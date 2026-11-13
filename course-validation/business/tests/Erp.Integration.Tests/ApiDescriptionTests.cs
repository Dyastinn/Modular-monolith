using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;

namespace Erp.Integration.Tests;

// The OpenAPI document is generated from the endpoints themselves. TypedResults put every possible answer into it,
// so a client knows a missing order is a 404 without reading our code.
[Collection("Integration")]
public class ApiDescriptionTests(CustomWebAppFactory factory)
{
    [Fact]
    public async Task The_api_description_lists_each_endpoints_possible_answers()
    {
        var document = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/openapi/v1.json");

        var getOrder = document.GetProperty("paths").GetProperty("/api/sales-orders/{id}").GetProperty("get").GetProperty("responses");
        getOrder.EnumerateObject().Select(r => r.Name).Should().Contain(["200", "404"]);

        var createOrder = document.GetProperty("paths").GetProperty("/api/sales-orders").GetProperty("post").GetProperty("responses");
        createOrder.EnumerateObject().Select(r => r.Name).Should().Contain("201");

        document.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _).Should().BeTrue();
    }

    [Fact]
    public async Task The_api_browser_page_is_served_in_development()
    {
        var response = await factory.CreateClient().GetAsync("/scalar");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("scalar", "it's the API browser, not the app's fallback page");
    }
}
