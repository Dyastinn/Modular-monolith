using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Sla.Domain;

// The two deadlines of one ticket: answer it, and resolve it. A target counts open time only: the due time is the business
// calendar's answer to "this much open time after the start". The events that move a clock arrive at least once, in any
// order, so each method guards on the event's own time and a stale call changes nothing.
internal sealed class SlaClock : AggregateRoot<TicketId>
{
    // A target is at risk when less than a quarter of its open time is left, which is three quarters of the way through it.
    private const double AtRiskAfterFraction = 0.75;

    private SlaClock() { }

    public Priority Priority { get; private set; }
    public DateTime StartedUtc { get; private set; }
    public TimeSpan RespondWithin { get; private set; }
    public TimeSpan ResolveWithin { get; private set; }
    public DateTime ResponseDueUtc { get; private set; }
    public DateTime ResponseAtRiskUtc { get; private set; }
    public DateTime? RespondedUtc { get; private set; }
    public DateTime ResolveDueUtc { get; private set; }
    public DateTime ResolveAtRiskUtc { get; private set; }
    public DateTime? ResolvedUtc { get; private set; }
    public bool Cancelled { get; private set; }
    public int Version { get; private set; }

    // Kept so a late event can be recognized as late.
    public DateTime? LastReopenedUtc { get; private set; }
    public DateTime? PriorityChangedUtc { get; private set; }

    // null while the target is still running, or when the ticket was cancelled (it is no longer measured).
    public bool? ResponseMet
    {
        get
        {
            if (Cancelled || RespondedUtc is null) return null;
            return RespondedUtc <= ResponseDueUtc;
        }
    }

    public bool? ResolutionMet
    {
        get
        {
            if (Cancelled || ResolvedUtc is null) return null;
            return ResolvedUtc <= ResolveDueUtc;
        }
    }

    // The earliest deadline still running; null once nothing is.
    public DateTime? NextDueUtc
    {
        get
        {
            if (Cancelled) return null;
            if (RespondedUtc is null) return ResponseDueUtc;
            if (ResolvedUtc is null) return ResolveDueUtc;
            return null;
        }
    }

    // When the running target (the one NextDueUtc names) enters its last quarter.
    public DateTime? AtRiskFromUtc
    {
        get
        {
            if (Cancelled) return null;
            if (RespondedUtc is null) return ResponseAtRiskUtc;
            if (ResolvedUtc is null) return ResolveAtRiskUtc;
            return null;
        }
    }

    // The ticket was created: both targets start running. The time is the ticket's own, never the handler's "now".
    public static SlaClock Start(TicketId ticketId, Priority priority, DateTime createdUtc, TimeSpan respondWithin, TimeSpan resolveWithin,
        BusinessCalendar calendar)
    {
        createdUtc = WholeMicroseconds(createdUtc);
        var clock = new SlaClock
        {
            Id = ticketId,
            Priority = priority,
            StartedUtc = createdUtc,
        };
        clock.SetResponseTarget(respondWithin, calendar);
        clock.SetResolveTarget(resolveWithin, calendar);
        clock.Changed();
        return clock;
    }

    // The first answer to the requester. Only the first counts.
    public void RecordResponse(DateTime atUtc)
    {
        if (Cancelled) return;
        if (RespondedUtc is not null) return;

        RespondedUtc = WholeMicroseconds(atUtc);
        Changed();
    }

    // The ticket was resolved. Resolving does not count as a response: only an agent's public reply stops the response target
    // (decision of 2026-11-20), so a ticket fixed with no reply keeps showing its unanswered target.
    // A resolution that happened before the last reopen is old news, and one already known is a repeat.
    public void RecordResolution(DateTime atUtc)
    {
        atUtc = WholeMicroseconds(atUtc);
        if (Cancelled) return;
        if (ResolvedUtc is not null) return;
        if (LastReopenedUtc is not null && atUtc < LastReopenedUtc) return;

        ResolvedUtc = atUtc;
        Changed();
    }

    // The ticket was reopened: the resolve target runs again, with the same due time. A reopen that happened before the
    // resolution it would undo is old news; one that arrives before its resolution is only remembered.
    public void Reopen(DateTime atUtc)
    {
        atUtc = WholeMicroseconds(atUtc);
        if (Cancelled) return;
        if (ResolvedUtc is not null && atUtc <= ResolvedUtc) return;

        if (LastReopenedUtc is null || atUtc > LastReopenedUtc) LastReopenedUtc = atUtc;
        if (ResolvedUtc is null) return;

        ResolvedUtc = null;
        Changed();
    }

    // Cancelled wins over everything, whatever order the events came in. Nothing is measured any more.
    public void Cancel()
    {
        if (Cancelled) return;

        Cancelled = true;
        Changed();
    }

    // The priority changed: the targets still running are recomputed from the start, with the policy's new numbers. A
    // target that is already finished keeps its due time. An older change that arrives late is ignored.
    public void ChangeTargets(Priority priority, DateTime atUtc, TimeSpan respondWithin, TimeSpan resolveWithin, BusinessCalendar calendar)
    {
        atUtc = WholeMicroseconds(atUtc);
        if (Cancelled) return;
        if (PriorityChangedUtc is not null && atUtc <= PriorityChangedUtc) return;

        PriorityChangedUtc = atUtc;
        var before = Snapshot();

        Priority = priority;
        if (RespondedUtc is null) SetResponseTarget(respondWithin, calendar);
        if (ResolvedUtc is null) SetResolveTarget(resolveWithin, calendar);

        if (before == Snapshot()) return;

        Changed();
    }

    // The rules changed (the calendar, or the policy's numbers): the targets still running are worked out again from the start.
    // A target that is already finished keeps its due time and its numbers.
    public void Recalculate(TimeSpan respondWithin, TimeSpan resolveWithin, BusinessCalendar calendar)
    {
        if (Cancelled) return;
        var before = Snapshot();

        if (RespondedUtc is null) SetResponseTarget(respondWithin, calendar);
        if (ResolvedUtc is null) SetResolveTarget(resolveWithin, calendar);

        if (before == Snapshot()) return;

        Changed();
    }

    // True while at least one target is still running: a clock whose deadlines can still move.
    public bool IsRunning => !Cancelled && (RespondedUtc is null || ResolvedUtc is null);

    private (Priority, TimeSpan, DateTime, DateTime, TimeSpan, DateTime, DateTime) Snapshot()
    {
        return (Priority, RespondWithin, ResponseDueUtc, ResponseAtRiskUtc, ResolveWithin, ResolveDueUtc, ResolveAtRiskUtc);
    }

    private void SetResponseTarget(TimeSpan within, BusinessCalendar calendar)
    {
        RespondWithin = within;
        ResponseDueUtc = WholeMicroseconds(calendar.Add(StartedUtc, within));
        ResponseAtRiskUtc = WholeMicroseconds(calendar.Add(StartedUtc, within * AtRiskAfterFraction));
    }

    private void SetResolveTarget(TimeSpan within, BusinessCalendar calendar)
    {
        ResolveWithin = within;
        ResolveDueUtc = WholeMicroseconds(calendar.Add(StartedUtc, within));
        ResolveAtRiskUtc = WholeMicroseconds(calendar.Add(StartedUtc, within * AtRiskAfterFraction));
    }

    // PostgreSQL keeps whole microseconds; a time with finer ticks would compare differently before and after the clock is
    // saved, and a reply made exactly at the due time would count as late. Every time that comes in is cut the same way.
    private static DateTime WholeMicroseconds(DateTime value)
    {
        const long TicksPerMicrosecond = 10;
        return new DateTime(value.Ticks - value.Ticks % TicksPerMicrosecond, value.Kind);
    }

    private void Changed()
    {
        Version++;
        Raise(new DeadlinesChangedEvent(Id, Version, ResponseDueUtc, ResponseMet, ResolveDueUtc, ResolutionMet,
            NextDueUtc, AtRiskFromUtc));
    }
}
