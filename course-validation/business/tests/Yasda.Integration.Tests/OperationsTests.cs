using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Dapper;
using Yasda.Shared.BuildingBlocks;
using Yasda.Shared.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Yasda.Integration.Tests;

// What keeps the running system honest: the outbox, health, tracing, limits and the database role.
[Collection("Integration")]
public class OperationsTests(CustomWebAppFactory factory)
{
    private sealed record Problem(string TraceId);

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
            INSERT INTO identity.outbox_message (id, type, payload, occurred_utc, attempts)
            VALUES (@id, 'Yasda.NoSuchEvent, Yasda.NoSuchAssembly', '{}', now(), 0)
            """, new { id });

        var attempts = await Eventually(
            () => conn.ExecuteScalarAsync<int>("SELECT attempts FROM identity.outbox_message WHERE id = @id", new { id }),
            a => a >= 10);
        await Task.Delay(1000);   // a few more dispatcher ticks

        var row = await conn.QuerySingleAsync<(int Attempts, DateTime? Processed, string? Error)>(
            "SELECT attempts, processed_utc, last_error FROM identity.outbox_message WHERE id = @id", new { id });
        attempts.Should().Be(10);
        row.Attempts.Should().Be(10, "after MaxAttempts it is left alone");
        row.Processed.Should().BeNull();
        row.Error.Should().Contain("NoSuchAssembly");
    }

    // ---- Lesson 14: observability ----

    [Fact]
    public async Task The_ready_check_fails_when_the_database_cannot_be_reached()
    {
        await using var unreachable = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=yasda;Username=x;Password=x;Timeout=2");

        var result = await new DatabaseHealthCheck(unreachable).CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    // The id in the error body is the id in the header is the id in the logs: one search finds the whole story.
    [Fact]
    public async Task An_error_carries_the_same_trace_id_as_its_response_header()
    {
        var admin = await factory.ClientFor("admin@yasda.local");
        var response = await admin.GetAsync("/api/users/999999");

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
            statuses.Add((await client.PostAsJsonAsync("/api/auth/login", new { email = "agent@yasda.local", password = "wrong-password!" })).StatusCode);

        statuses.Should().Equal(HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized,
            HttpStatusCode.TooManyRequests);
    }

    // The app's own login can change rows, never the schema: a compromised app can't drop a table.
    [Theory]
    [InlineData("CREATE TABLE identity.intruder (id int)")]
    [InlineData("DROP TABLE identity.sign_in_attempt")]
    [InlineData("ALTER TABLE identity.sign_in_attempt ADD COLUMN intruder int")]
    public async Task The_app_role_cannot_change_the_schema(string ddl)
    {
        await using var conn = factory.OpenDatabase();
        var act = () => conn.ExecuteAsync(ddl);
        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().BeOneOf("42501", "42P01");
    }
}
