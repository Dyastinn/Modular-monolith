using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Identity.Domain;

// A team's name changed. Saved to the outbox with the rename; the publishing class tells the other modules afterwards.
internal sealed record TeamRenamedEvent(TeamId TeamId, string Name) : IDomainEvent;
