using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Collections.Concurrent;
using System.Reflection;
using Dapper;
using Yasda.Shared.BuildingBlocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Yasda.Shared.Infrastructure;

public sealed class OutboxOptions
{
    public int PollIntervalMs { get; set; } = 2000;
    public int BatchSize { get; set; } = 50;
    public int MaxAttempts { get; set; } = 10;
}

// Delivers outbox events to their module's IDomainEventHandler<T> classes after commit. At-least-once: a handler may see an event twice, so handlers must be
// idempotent. FOR UPDATE SKIP LOCKED keeps several app instances from taking the same message. After
// MaxAttempts failures a message is left, with its last_error, for a person to look at.
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopes,
    NpgsqlDataSource dataSource,
    IConfiguration config,
    OutboxBacklog backlog,
    TimeProvider time,
    ILogger<OutboxDispatcher> log) : BackgroundService
{
    private static readonly Counter<long> Dispatched = YasdaTelemetry.Meter.CreateCounter<long>("yasda.outbox.dispatched");
    private static readonly Counter<long> Failed = YasdaTelemetry.Meter.CreateCounter<long>("yasda.outbox.failed");

    // What the last tick saw in each module's outbox. A gauge is read when the metrics are collected, so the latest
    // numbers wait here until then.
    private static readonly ConcurrentDictionary<string, long> PendingBySchema = new();
    private static readonly ConcurrentDictionary<string, long> ParkedBySchema = new();

    static OutboxDispatcher()
    {
        YasdaTelemetry.Meter.CreateObservableGauge("yasda.outbox.pending", () => ReadGauge(PendingBySchema));
        YasdaTelemetry.Meter.CreateObservableGauge("yasda.outbox.parked", () => ReadGauge(ParkedBySchema));
    }

    private static List<Measurement<long>> ReadGauge(ConcurrentDictionary<string, long> bySchema)
    {
        var measurements = new List<Measurement<long>>();
        foreach (var pair in bySchema)
            measurements.Add(new Measurement<long>(pair.Value, new KeyValuePair<string, object?>("schema", pair.Key)));
        return measurements;
    }

    private readonly OutboxOptions _options = config.GetSection("Outbox").Get<OutboxOptions>() ?? new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        List<string> schemas;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var modules = scope.ServiceProvider.GetServices<IModuleDatabase>();
            schemas = modules.Select(m => m.Schema).ToList();
        }

        // Once right away (events may have waited through a restart), then on every tick.
        await DispatchAllAsync(schemas, stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.PollIntervalMs));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await DispatchAllAsync(schemas, stoppingToken);
        }
    }

    private async Task DispatchAllAsync(List<string> schemas, CancellationToken stoppingToken)
    {
        foreach (var schema in schemas)
        {
            try
            {
                // A full batch means more may be waiting, so go again until a batch comes back short.
                var moreWaiting = true;
                while (moreWaiting)
                {
                    var delivered = await DispatchBatchAsync(schema, stoppingToken);
                    moreWaiting = delivered == _options.BatchSize;
                }

                var counts = await backlog.CountAsync(schema, stoppingToken);
                PendingBySchema[schema] = counts.Pending;
                ParkedBySchema[schema] = counts.Parked;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogError(ex, "Outbox dispatch for schema {Schema} failed; will retry next tick", schema);
            }
        }
    }

    private sealed record Row(Guid Id, string Type, string Payload, string? TraceParent);

    public async Task<int> DispatchBatchAsync(string schema, CancellationToken ct)
    {
        var table = OutboxTable.NameOf(schema);

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var rows = await conn.QueryAsync<Row>(
            $"""
            SELECT id, type, payload, trace_parent AS TraceParent FROM {table}
            WHERE processed_utc IS NULL AND attempts < @MaxAttempts
            ORDER BY occurred_utc
            LIMIT @BatchSize
            FOR UPDATE SKIP LOCKED
            """,
            new { _options.MaxAttempts, _options.BatchSize }, tx);
        var batch = rows.ToList();

        foreach (var message in batch)
        {
            // The delivery joins the trace of the request that raised the event, so one trace shows both.
            ActivityContext requestContext = default;
            if (message.TraceParent is not null) ActivityContext.TryParse(message.TraceParent, null, out requestContext);
            using var activity = YasdaTelemetry.Source.StartActivity("outbox deliver", ActivityKind.Consumer, requestContext);
            activity?.SetTag("yasda.outbox.type", message.Type);
            try
            {
                var domainEvent = OutboxMessage.Deserialize(message.Type, message.Payload);
                await using var scope = scopes.CreateAsyncScope();
                await DeliverAsync(scope.ServiceProvider, domainEvent, ct);

                await conn.ExecuteAsync($"UPDATE {table} SET processed_utc = @Now WHERE id = @Id",
                    new { Now = time.GetUtcNow().UtcDateTime, message.Id }, tx);
                Dispatched.Add(1);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                await conn.ExecuteAsync($"UPDATE {table} SET attempts = attempts + 1, last_error = @Error WHERE id = @Id",
                    new { Error = ex.ToString(), message.Id }, tx);
                Failed.Add(1);
                log.LogError(ex, "Outbox message {MessageId} ({Type}) failed", message.Id, message.Type);
            }
        }

        await tx.CommitAsync(ct);
        return batch.Count;
    }

    // The event's type is known only at run time (it was read from a row), so the typed method is picked by reflection.
    private static readonly MethodInfo DeliverTyped =
        typeof(OutboxDispatcher).GetMethod(nameof(DeliverToHandlersAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static Task DeliverAsync(IServiceProvider services, IDomainEvent domainEvent, CancellationToken ct) =>
        (Task)DeliverTyped.MakeGenericMethod(domainEvent.GetType()).Invoke(null, [services, domainEvent, ct])!;

    private static async Task DeliverToHandlersAsync<TEvent>(IServiceProvider services, TEvent domainEvent, CancellationToken ct)
        where TEvent : IDomainEvent
    {
        foreach (var handler in services.GetServices<IDomainEventHandler<TEvent>>())
            await handler.HandleAsync(domainEvent, ct);
    }
}
