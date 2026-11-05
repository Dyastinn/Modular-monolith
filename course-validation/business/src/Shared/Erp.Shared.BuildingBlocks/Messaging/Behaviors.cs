using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Erp.Shared.BuildingBlocks;

// Wraps every command and query with the three signals operations needs: a trace span (where did the time go?),
// a duration metric (how often, how slow, how many failures?) and a log line. Registered OUTERMOST in
// Program.cs so it also sees requests that fail validation.
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
            // Rejections the user caused (validation, business rules) are expected; anything else is a failure.
            outcome = ex is ValidationException or DomainException or NotFoundException or ConcurrencyException
                ? "rejected" : "error";
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            log.LogWarning(ex, "{Request} {Outcome} after {ElapsedMs} ms", name, outcome, sw.ElapsedMilliseconds);
            throw;   // telemetry observes; it never swallows
        }
        finally
        {
            Duration.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("request", name), new KeyValuePair<string, object?>("outcome", outcome));
        }
    }
}

// Runs every FluentValidation validator for the message; throws before the handler if any fail.
public sealed class ValidationBehavior<TMessage, TResponse>(IEnumerable<IValidator<TMessage>> validators)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken ct)
    {
        var failures = new List<FluentValidation.Results.ValidationFailure>();
        foreach (var validator in validators)
            failures.AddRange((await validator.ValidateAsync(message, ct)).Errors);

        if (failures.Count != 0)
            throw new ValidationException(failures);

        return await next(message, ct);
    }
}
