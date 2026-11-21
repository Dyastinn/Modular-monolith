using Yasda.Modules.Tickets.Contracts;
using Yasda.Modules.Tickets.Domain;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Features;

// One small class per domain event: it turns what Tickets' domain recorded into the public fact other modules hear.
// They run in the outbox delivery, after the ticket's change has committed. Their names say what they do, and none
// ends in "Handler" (those are the classes an endpoint calls, registered one by one).

internal sealed class PublishTicketCreated(IIntegrationEventPublisher publisher) : IDomainEventHandler<TicketCreatedEvent>
{
    public async Task HandleAsync(TicketCreatedEvent domainEvent, CancellationToken ct)
    {
        var priority = PublishedPriority.From(domainEvent.Priority);
        await publisher.PublishAsync(new TicketCreated(domainEvent.TicketId.Value, priority, domainEvent.CreatedUtc, domainEvent.CategoryId), ct);
    }
}

internal sealed class PublishFirstAgentReply(IIntegrationEventPublisher publisher) : IDomainEventHandler<FirstAgentReplyEvent>
{
    public async Task HandleAsync(FirstAgentReplyEvent domainEvent, CancellationToken ct)
    {
        await publisher.PublishAsync(new FirstAgentReply(domainEvent.TicketId.Value, domainEvent.RepliedUtc), ct);
    }
}

internal sealed class PublishTicketResolved(IIntegrationEventPublisher publisher) : IDomainEventHandler<TicketResolvedEvent>
{
    public async Task HandleAsync(TicketResolvedEvent domainEvent, CancellationToken ct)
    {
        await publisher.PublishAsync(new TicketResolved(domainEvent.TicketId.Value, domainEvent.ResolvedUtc), ct);
    }
}

internal sealed class PublishTicketReopened(IIntegrationEventPublisher publisher) : IDomainEventHandler<TicketReopenedEvent>
{
    public async Task HandleAsync(TicketReopenedEvent domainEvent, CancellationToken ct)
    {
        await publisher.PublishAsync(new TicketReopened(domainEvent.TicketId.Value, domainEvent.ReopenedUtc), ct);
    }
}

internal sealed class PublishTicketCancelled(IIntegrationEventPublisher publisher) : IDomainEventHandler<TicketCancelledEvent>
{
    public async Task HandleAsync(TicketCancelledEvent domainEvent, CancellationToken ct)
    {
        await publisher.PublishAsync(new TicketCancelled(domainEvent.TicketId.Value, domainEvent.CancelledUtc), ct);
    }
}

internal sealed class PublishTicketPriorityChanged(IIntegrationEventPublisher publisher) : IDomainEventHandler<TicketPriorityChangedEvent>
{
    public async Task HandleAsync(TicketPriorityChangedEvent domainEvent, CancellationToken ct)
    {
        var priority = PublishedPriority.From(domainEvent.Priority);
        await publisher.PublishAsync(new TicketPriorityChanged(domainEvent.TicketId.Value, priority, domainEvent.ChangedUtc), ct);
    }
}

internal sealed class PublishTicketCategoryChanged(IIntegrationEventPublisher publisher) : IDomainEventHandler<TicketCategoryChangedEvent>
{
    public async Task HandleAsync(TicketCategoryChangedEvent domainEvent, CancellationToken ct)
    {
        await publisher.PublishAsync(new TicketCategoryChanged(domainEvent.TicketId.Value, domainEvent.CategoryId, domainEvent.ChangedUtc), ct);
    }
}

internal sealed class PublishTicketPending(IIntegrationEventPublisher publisher) : IDomainEventHandler<TicketPendingEvent>
{
    public async Task HandleAsync(TicketPendingEvent domainEvent, CancellationToken ct)
    {
        await publisher.PublishAsync(new TicketPending(domainEvent.TicketId.Value, domainEvent.PendingUtc), ct);
    }
}

internal sealed class PublishTicketResumed(IIntegrationEventPublisher publisher) : IDomainEventHandler<TicketResumedEvent>
{
    public async Task HandleAsync(TicketResumedEvent domainEvent, CancellationToken ct)
    {
        await publisher.PublishAsync(new TicketResumed(domainEvent.TicketId.Value, domainEvent.ResumedUtc), ct);
    }
}

// Tickets' domain keeps its own priority enum; the public one lives in the Contracts project. Both have the same names.
internal static class PublishedPriority
{
    public static Contracts.TicketPriority From(Domain.TicketPriority priority)
    {
        switch (priority)
        {
            case Domain.TicketPriority.Low: return Contracts.TicketPriority.Low;
            case Domain.TicketPriority.Normal: return Contracts.TicketPriority.Normal;
            case Domain.TicketPriority.High: return Contracts.TicketPriority.High;
            case Domain.TicketPriority.Critical: return Contracts.TicketPriority.Critical;
            default: throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unknown priority.");
        }
    }
}
