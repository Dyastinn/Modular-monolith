using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Dapper;
using Erp.Shared.BuildingBlocks;
using Erp.Shared.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Erp.Integration.Tests;

// What keeps the running system honest: the outbox, health, tracing, limits and the database role.
[Collection("Integration")]
public class OperationsTests(CustomWebAppFactory factory)
{
    private sealed record Problem(string TraceId);
    private sealed record Created(long Id);

    private static async Task<T> Eventually<T>(Func<Task<T>> read, Func<T, bool> done)
    {
        var value = await read();
        for (var i = 0; i < 75 && !done(value); i++)
        {
            await Task.Delay(200);
            value = await read();
        }
        return value;
    }

    // ---- Lesson 13: events and the outbox ----

    // A message that can never be delivered must not block the others, and must not be retried forever.
    [Fact]
    public async Task A_message_that_keeps_failing_is_parked_with_its_error()
    {
        var id = Guid.NewGuid();
        await using var conn = factory.OpenDatabase();
        await conn.ExecuteAsync(
            """
            INSERT INTO sales.outbox_message (id, type, payload, occurred_utc, attempts)
            VALUES (@id, 'Erp.NoSuchEvent, Erp.NoSuchAssembly', '{}', now(), 0)
            """, new { id });

        var attempts = await Eventually(
            () => conn.ExecuteScalarAsync<int>("SELECT attempts FROM sales.outbox_message WHERE id = @id", new { id }),
            a => a >= 10);
        await Task.Delay(1000);   // a few more dispatcher ticks

        var row = await conn.QuerySingleAsync<(int Attempts, DateTime? Processed, string? Error)>(
            "SELECT attempts, processed_utc, last_error FROM sales.outbox_message WHERE id = @id", new { id });
        attempts.Should().Be(10);
        row.Attempts.Should().Be(10, "after MaxAttempts it is left alone");
        row.Processed.Should().BeNull();
        row.Error.Should().Contain("NoSuchAssembly");
    }

    // At-least-once delivery means a handler can see the same event twice. Deliver everything again: nothing may change.
    [Fact]
    public async Task Delivering_every_event_a_second_time_changes_nothing()
    {
        const string snapshot = """
            SELECT (SELECT COUNT(*) FROM invoicing.invoice)::text
                || '|' || (SELECT COALESCE(SUM(total), 0) FROM invoicing.invoice)::text
                || '|' || (SELECT COALESCE(SUM(outstanding_balance), 0) FROM sales.customer)::text
                || '|' || (SELECT COUNT(*) FROM sales.applied_payment)::text
                || '|' || (SELECT COUNT(*) FROM sales.item)::text
                || '|' || (SELECT COALESCE(SUM(on_hand), 0) FROM inventory.item)::text
            """;
        string[] schemas;
        await using (var scope = factory.Services.CreateAsyncScope())
            schemas = scope.ServiceProvider.GetServices<IModuleDatabase>().Select(m => m.Schema).ToArray();
        await using var conn = factory.OpenDatabase();

        // Raise at least one event of our own, so the test doesn't depend on what ran before it.
        var rep = await factory.ClientFor("rep@erp.local");
        var created = await rep.PostAsJsonAsync("/api/sales-orders", new { customerId = 5, lines = new[] { new { itemId = 1, quantity = 1 } } });
        var orderId = (await created.Content.ReadFromJsonAsync<Created>())!.Id;
        (await rep.PostAsync($"/api/sales-orders/{orderId}/submit", null)).EnsureSuccessStatusCode();   // auto-approved

        // Let it, and whatever earlier tests raised, finish first.
        async Task<long> Waiting() => await conn.ExecuteScalarAsync<long>(string.Join(" + ", schemas.Select(s =>
            $"(SELECT COUNT(*) FROM {s}.outbox_message WHERE processed_utc IS NULL AND attempts < 10)")).Insert(0, "SELECT "));
        await Eventually(Waiting, n => n == 0);
        var before = await conn.ExecuteScalarAsync<string>(snapshot);

        var redelivered = 0;
        foreach (var schema in schemas)
            redelivered += await conn.ExecuteAsync($"UPDATE {schema}.outbox_message SET processed_utc = NULL WHERE processed_utc IS NOT NULL");
        (await Eventually(Waiting, n => n == 0)).Should().Be(0);

        redelivered.Should().BeGreaterThan(0, "this test raised an approval");
        (await conn.ExecuteScalarAsync<string>(snapshot)).Should().Be(before);
    }

    // ---- Lesson 14: observability ----

    [Fact]
    public async Task The_ready_check_fails_when_the_database_cannot_be_reached()
    {
        await using var unreachable = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=erp;Username=x;Password=x;Timeout=2");

        var result = await new DatabaseHealthCheck(unreachable).CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    // The id in the error body is the id in the header is the id in the logs: one search finds the whole story.
    [Fact]
    public async Task An_error_carries_the_same_trace_id_as_its_response_header()
    {
        var rep = await factory.ClientFor("rep@erp.local");
        var response = await rep.GetAsync("/api/sales-orders/999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var header = response.Headers.GetValues("X-Trace-Id").Single();
        (await response.Content.ReadFromJsonAsync<Problem>())!.TraceId.Should().Be(header);
    }

    // ---- Lesson 23: security hardening ----

    [Fact]
    public async Task Too_many_sign_in_attempts_get_429()
    {
        await using var strict = factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:AuthRequestsPerMinute", "3"));
        var client = strict.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
            statuses.Add((await client.PostAsJsonAsync("/api/auth/login", new { email = "rep@erp.local", password = "wrong-password!" })).StatusCode);

        statuses.Should().Equal(HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized,
            HttpStatusCode.TooManyRequests);
    }

    // The app's own login can change rows, never the schema: a compromised app can't drop a table.
    [Theory]
    [InlineData("CREATE TABLE sales.intruder (id int)")]
    [InlineData("DROP TABLE sales.customer")]
    [InlineData("ALTER TABLE sales.customer ADD COLUMN intruder int")]
    public async Task The_app_role_cannot_change_the_schema(string ddl)
    {
        await using var conn = factory.OpenDatabase();
        var act = () => conn.ExecuteAsync(ddl);
        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().BeOneOf("42501", "42P01");
    }
}
