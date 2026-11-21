using Yasda.Modules.Sla.Contracts;
using Yasda.Modules.Sla.Domain;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Sla.Features;

// What the scheduler found, told to the other modules (Tickets mails the people who need to know). Each is raised once per target.
internal sealed class PublishSlaBreachImminent(IIntegrationEventPublisher publisher) : IDomainEventHandler<SlaBreachImminentEvent>
{
    public async Task HandleAsync(SlaBreachImminentEvent domainEvent, CancellationToken ct)
    {
        await publisher.PublishAsync(new SlaBreachImminent(domainEvent.TicketId.Value, domainEvent.Target.ToString(), domainEvent.DueUtc), ct);
    }
}

internal sealed class PublishSlaBreached(IIntegrationEventPublisher publisher) : IDomainEventHandler<SlaBreachedEvent>
{
    public async Task HandleAsync(SlaBreachedEvent domainEvent, CancellationToken ct)
    {
        await publisher.PublishAsync(new SlaBreached(domainEvent.TicketId.Value, domainEvent.Target.ToString(), domainEvent.DueUtc), ct);
    }
}

// A clock's deadlines changed: tell the modules that show them. Tickets keeps its own copy for the queues.
internal sealed class PublishDeadlinesChanged(IIntegrationEventPublisher publisher) : IDomainEventHandler<DeadlinesChangedEvent>
{
    public async Task HandleAsync(DeadlinesChangedEvent domainEvent, CancellationToken ct)
    {
        var published = new DeadlinesChanged(domainEvent.TicketId.Value, domainEvent.Version,
            domainEvent.ResponseDueUtc, domainEvent.ResponseMet, domainEvent.ResolveDueUtc, domainEvent.ResolutionMet,
            domainEvent.NextDueUtc, domainEvent.AtRiskFromUtc, domainEvent.Paused);
        await publisher.PublishAsync(published, ct);
    }
}
