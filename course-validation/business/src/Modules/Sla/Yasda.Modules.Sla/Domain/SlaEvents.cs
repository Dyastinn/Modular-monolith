using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Sla.Domain;

// A clock's deadlines changed. Raised by every change that alters what the clock tells the rest of the system.
// Version counts the changes of one ticket's clock, so the receiver can ignore an older answer that arrives late.
// Which of a ticket's two deadlines an event is about.
internal enum SlaTarget
{
    Response,
    Resolve,
}

// A target entered its last quarter, or ran out of time. Each is raised once per target, whatever happens to the clock afterwards.
internal sealed record SlaBreachImminentEvent(TicketId TicketId, SlaTarget Target, DateTime DueUtc) : IDomainEvent;

internal sealed record SlaBreachedEvent(TicketId TicketId, SlaTarget Target, DateTime DueUtc) : IDomainEvent;

internal sealed record DeadlinesChangedEvent(
    TicketId TicketId, int Version,
    DateTime ResponseDueUtc, bool? ResponseMet,
    DateTime ResolveDueUtc, bool? ResolutionMet,
    DateTime? NextDueUtc, DateTime? AtRiskFromUtc, bool Paused) : IDomainEvent;
