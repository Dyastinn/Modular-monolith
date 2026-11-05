using System.Diagnostics;
using System.Diagnostics.Metrics;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Erp.Shared.Infrastructure;

// A span, a duration metric and a log line for every command and query. Registered outermost.
public sealed class TelemetryBehavior<TMessage, TResponse>(ILogger<TelemetryBehavior<TMessage, TResponse>> log)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    private static readonly Histogram<double> Duration = ErpTelemetry.Meter.CreateHistogram<double>(
        "erp.request.duration", unit: "ms", description: "Time to handle a command or query");

    public async ValueTask<TResponse> Handle(
        TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken ct)
    {
        var name = typeof(TMessage).Name;
        using var activity = ErpTelemetry.Source.StartActivity(name);
        var sw = Stopwatch.StartNew();
        var outcome = "success";
        try
        {
            var response = await next(message, ct);
            log.LogInformation("{Request} handled in {ElapsedMs} ms", name, sw.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            // Rejections the caller caused are expected; anything else is a failure worth alerting on.
            outcome = ex is ValidationException or DomainException or NotFoundException or DbUpdateConcurrencyException
                ? "rejected" : "error";
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            log.LogWarning(ex, "{Request} {Outcome} after {ElapsedMs} ms", name, outcome, sw.ElapsedMilliseconds);
            throw;
        }
        finally
        {
            Duration.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("request", name), new KeyValuePair<string, object?>("outcome", outcome));
        }
    }
}
