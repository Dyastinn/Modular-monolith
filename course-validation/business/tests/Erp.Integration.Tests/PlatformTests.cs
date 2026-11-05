using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Dapper;
using Npgsql;

namespace Erp.Integration.Tests;

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
        var response = await (await factory.ClientFor("rep@erp.local")).GetAsync("/sales-orders");

        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle("DENY");
        response.Headers.GetValues("X-Trace-Id").Single().Should().HaveLength(32);
    }

    [Fact]
    public async Task An_approval_event_is_delivered_through_the_outbox()
    {
        var rep = await factory.ClientFor("rep@erp.local");
        var create = await rep.PostAsJsonAsync("/sales-orders",
            new { customerId = 1, lines = new[] { new { itemId = 1, quantity = 1 } } });
        var id = (await create.Content.ReadFromJsonAsync<Created>())!.Id;
        await rep.PostAsync($"/sales-orders/{id}/submit", null);   // auto-approved → SalesOrderApproved

        await using var conn = new NpgsqlConnection(CustomWebAppFactory.TestConnectionString);
        const string sql = """
            SELECT processed_utc FROM sales.outbox_message
            WHERE type LIKE '%SalesOrderApproved%' AND (payload->'Id'->>'Value')::bigint = @id
            """;

        DateTime? processed = null;
        for (var attempt = 0; attempt < 50 && processed is null; attempt++)
        {
            processed = await conn.QuerySingleOrDefaultAsync<DateTime?>(sql, new { id });
            if (processed is null) await Task.Delay(200);
        }

        processed.Should().NotBeNull("the dispatcher should deliver the event within a few seconds");
    }

    private sealed record Created(long Id);
}
