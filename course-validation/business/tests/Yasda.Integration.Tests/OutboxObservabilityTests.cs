using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using AwesomeAssertions;
using Dapper;

namespace Yasda.Integration.Tests;

// Lesson 14: what the outbox shows the people who run the system. An event that was never delivered is a clock that never
// started or a mail nobody got, and nothing in the request that caused it will say so. These tests are why the outbox
// reports a trace, a backlog and a health status.
[Collection("Integration")]
public class OutboxObservabilityTests(CustomWebAppFactory factory)
{
    private static readonly string[] Schemas = ["identity", "tickets", "sla"];

    // Messages the dispatcher gave up on, from this test or an earlier one, would hide what these tests look for.
    private static async Task RemoveParkedMessagesAsync(Npgsql.NpgsqlConnection conn)
    {
        foreach (var schema in Schemas)
            await conn.ExecuteAsync($"DELETE FROM {schema}.outbox_message WHERE processed_utc IS NULL AND attempts >= 10");
    }

    private static async Task ParkAMessageAsync(Npgsql.NpgsqlConnection conn, string schema)
    {
        await conn.ExecuteAsync(
            $$"""
            INSERT INTO {{schema}}.outbox_message (id, type, payload, occurred_utc, attempts, last_error)
            VALUES (@id, 'Yasda.NoSuchEvent, Yasda.NoSuchAssembly', '{}', now(), 10, 'gave up')
            """, new { id = Guid.NewGuid() });
    }

    [Fact]
    public async Task An_event_is_delivered_in_the_trace_of_the_request_that_raised_it()
    {
        var delivered = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "Yasda" or "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => { if (activity.OperationName == "outbox deliver") delivered.Add(activity); },
        };
        ActivitySource.AddActivityListener(listener);

        var traceId = ActivityTraceId.CreateRandom();
        var requester = await factory.ClientFor("requester@yasda.local");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/tickets")
        {
            Content = JsonContent.Create(new { subject = $"Traced {Guid.NewGuid():N}", message = "Please help.", priority = "Normal" }),
        };
        request.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");
        (await requester.SendAsync(request)).EnsureSuccessStatusCode();

        var joined = await Waiting.Eventually(
            () => Task.FromResult(delivered.Where(a => a.TraceId == traceId).ToList()),
            found => found.Count > 0);
        joined.Should().NotBeEmpty("the delivery of a ticket's events carries the trace id of the request that filed it");
    }

    [Fact]
    public async Task The_ready_check_is_degraded_while_a_message_is_parked_and_healthy_again_once_it_is_removed()
    {
        await using var conn = factory.OpenDatabase();
        await RemoveParkedMessagesAsync(conn);
        var client = factory.CreateClient();
        (await client.GetStringAsync("/health/ready")).Should().Be("Healthy");

        await ParkAMessageAsync(conn, "sla");
        (await client.GetStringAsync("/health/ready")).Should().Be("Degraded");

        await RemoveParkedMessagesAsync(conn);
        (await client.GetStringAsync("/health/ready")).Should().Be("Healthy");
    }

    [Fact]
    public async Task The_parked_gauge_counts_the_messages_given_up_on_per_module()
    {
        await using var conn = factory.OpenDatabase();
        await RemoveParkedMessagesAsync(conn);
        await ParkAMessageAsync(conn, "sla");
        _ = factory.Server;   // the app is what runs the dispatcher

        var seen = new ConcurrentDictionary<string, long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Name == "yasda.outbox.parked") meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            foreach (var tag in tags)
                if (tag.Key == "schema") seen[tag.Value!.ToString()!] = value;
        });
        listener.Start();

        var parkedInSla = await Waiting.Eventually(
            () =>
            {
                listener.RecordObservableInstruments();
                return Task.FromResult(seen.GetValueOrDefault("sla"));
            },
            count => count == 1);
        await RemoveParkedMessagesAsync(conn);

        parkedInSla.Should().Be(1);
    }
}
