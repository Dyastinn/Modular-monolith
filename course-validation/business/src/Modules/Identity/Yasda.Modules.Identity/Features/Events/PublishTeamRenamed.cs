using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Identity.Domain;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Identity.Features;

// A team was renamed: tell the modules that keep a copy of its name. Runs in the outbox delivery, after the rename committed.
internal sealed class PublishTeamRenamed(IIntegrationEventPublisher publisher) : IDomainEventHandler<TeamRenamedEvent>
{
    public async Task HandleAsync(TeamRenamedEvent domainEvent, CancellationToken ct)
    {
        await publisher.PublishAsync(new TeamRenamed(domainEvent.TeamId.Value, domainEvent.Name), ct);
    }
}
