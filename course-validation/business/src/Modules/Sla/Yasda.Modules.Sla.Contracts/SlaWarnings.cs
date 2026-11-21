using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Sla.Contracts;

// A target is entering its last quarter, and nobody has met it yet. Sent once per target. Target is "Response" or "Resolve".
public sealed record SlaBreachImminent(long TicketId, string Target, DateTime DueUtc) : IIntegrationEvent;

// A target's due time has passed without it being met. Sent once per target.
public sealed record SlaBreached(long TicketId, string Target, DateTime DueUtc) : IIntegrationEvent;
