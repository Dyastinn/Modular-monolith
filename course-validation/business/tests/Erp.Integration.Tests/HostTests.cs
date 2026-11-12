using System.Net;
using AwesomeAssertions;

namespace Erp.Integration.Tests;

// Lesson 01's promise: one host serves the browser app and the API, and never mixes them up.
[Collection("Integration")]
public class HostTests(CustomWebAppFactory factory)
{
    [Fact]
    public async Task The_home_page_is_the_app()
    {
        var response = await factory.CreateClient().GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
    }

    // The server has no file called /sales-orders/42: it sends the app, and the app's router shows that page.
    [Theory]
    [InlineData("/sales-orders")]
    [InlineData("/sales-orders/42")]
    public async Task A_page_address_is_the_app_too(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task An_unknown_api_address_is_404_whatever_the_method(string method)
    {
        var response = await factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), "/api/no-such-thing"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().NotBe("text/html");
    }

    [Theory]
    [InlineData("/api")]
    [InlineData("/api/")]
    [InlineData("/API/no-such-thing")]   // URLs are case-insensitive in ASP.NET Core, so the prefix is too
    public async Task The_api_prefix_itself_is_never_the_app(string path) =>
        (await factory.CreateClient().GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.NotFound);

    // A path with a file extension is a file. If it's missing, say so; the app's page would be a confusing answer.
    [Fact]
    public async Task A_missing_file_is_404_not_the_app() =>
        (await factory.CreateClient().GetAsync("/missing-script.js")).StatusCode.Should().Be(HttpStatusCode.NotFound);

    // Regression guard: a ReferenceOutputAssembly="false" reference once left the app without MudBlazor's styles.
    [Theory]
    [InlineData("/_content/MudBlazor/MudBlazor.min.css", "text/css")]
    [InlineData("/_content/MudBlazor/MudBlazor.min.js", "text/javascript")]
    public async Task The_component_library_files_are_served(string path, string mediaType)
    {
        var response = await factory.CreateClient().GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(mediaType);
    }
}
