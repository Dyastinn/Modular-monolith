using System.Diagnostics;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Erp.Shared.BuildingBlocks;

// Wraps every command and query. Registered in Program.cs (PipelineBehaviors), Logging OUTERMOST so it also
// logs requests that fail validation.
public sealed class LoggingBehavior<TMessage, TResponse>(ILogger<LoggingBehavior<TMessage, TResponse>> log)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken ct)
    {
        var name = typeof(TMessage).Name;
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await next(message, ct);
            log.LogInformation("{Request} handled in {ElapsedMs} ms", name, sw.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "{Request} failed after {ElapsedMs} ms", name, sw.ElapsedMilliseconds);
            throw;   // logging observes; it never swallows
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

public static class DomainEventPublishing
{
    // Publish an aggregate's events, then forget them. Call it AFTER the save has committed.
    public static async Task PublishDomainEventsAsync<TId>(
        this IPublisher publisher, AggregateRoot<TId> aggregate, CancellationToken ct) where TId : struct
    {
        var events = aggregate.DomainEvents.ToList();
        aggregate.ClearEvents();
        foreach (var e in events)
            await publisher.Publish(e, ct);   // Publish(object): dispatches on the event's runtime type
    }
}
