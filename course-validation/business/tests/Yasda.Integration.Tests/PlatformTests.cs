using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Dapper;
using Npgsql;

namespace Yasda.Integration.Tests;

[Collection("Integration")]
public class PlatformTests(CustomWebAppFactory factory)
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_report_healthy(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Responses_carry_security_headers_and_a_trace_id()
    {
        var response = await (await factory.ClientFor("agent@yasda.local")).GetAsync("/api/auth/me");

        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle("DENY");
        response.Headers.GetValues("X-Trace-Id").Single().Should().HaveLength(32);
    }

    [Fact]
    public async Task Pages_and_api_get_different_content_security_policies()
    {
        var client = factory.CreateClient();
        var pageCsp = (await client.GetAsync("/")).Headers.GetValues("Content-Security-Policy").Single();
        var apiCsp = (await client.GetAsync("/api/auth/me")).Headers.GetValues("Content-Security-Policy").Single();

        pageCsp.Should().Contain("script-src 'self' 'wasm-unsafe-eval'").And.NotContain("unsafe-inline' 'self' 'wasm");
        apiCsp.Should().StartWith("default-src 'none'");
    }
}
