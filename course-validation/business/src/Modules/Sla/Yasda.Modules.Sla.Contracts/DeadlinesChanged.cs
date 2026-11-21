using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Sla.Contracts;

// A ticket's deadlines, as Sla computes them. Version counts the changes of one ticket's clock, so a receiver can
// ignore an older answer that arrives late. A null Met means the target is still running; NextDueUtc is null once
// nothing is running or the clock is paused. AtRiskFromUtc is when the next target enters its last quarter. Paused means the ticket
// is waiting on the requester and no time is counting.
public sealed record DeadlinesChanged(
    long TicketId, int Version,
    DateTime ResponseDueUtc, bool? ResponseMet,
    DateTime ResolveDueUtc, bool? ResolutionMet,
    DateTime? NextDueUtc, DateTime? AtRiskFromUtc, bool Paused) : IIntegrationEvent;
