using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Sla.Domain;

// A clock's deadlines changed. Raised by every change that alters what the clock tells the rest of the system.
// Version counts the changes of one ticket's clock, so the receiver can ignore an older answer that arrives late.
internal sealed record DeadlinesChangedEvent(
    TicketId TicketId, int Version,
    DateTime ResponseDueUtc, bool? ResponseMet,
    DateTime ResolveDueUtc, bool? ResolutionMet,
    DateTime? NextDueUtc, DateTime? AtRiskFromUtc, bool Paused) : IDomainEvent;
