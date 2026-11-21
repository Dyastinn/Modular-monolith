using Yasda.Modules.Sla.Contracts;
using Yasda.Modules.Sla.Domain;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Sla.Features;

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
