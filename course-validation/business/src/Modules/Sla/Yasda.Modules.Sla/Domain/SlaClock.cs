using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Sla.Domain;

// The two deadlines of one ticket: answer it, and resolve it. Due times are the start plus the target for now (24
// hours a day); business hours replace that rule later. The events that move a clock arrive at least once, in any
// order, so each method guards on the event's own time and a stale call changes nothing.
internal sealed class SlaClock : AggregateRoot<TicketId>
{
    // A target is at risk when less than a quarter of its time is left.
    private const double AtRiskFraction = 0.25;

    private SlaClock() { }

    public Priority Priority { get; private set; }
    public DateTime StartedUtc { get; private set; }
    public TimeSpan RespondWithin { get; private set; }
    public TimeSpan ResolveWithin { get; private set; }
    public DateTime ResponseDueUtc { get; private set; }
    public DateTime? RespondedUtc { get; private set; }
    public DateTime ResolveDueUtc { get; private set; }
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
            if (RespondedUtc is null) return ResponseDueUtc - RespondWithin * AtRiskFraction;
            if (ResolvedUtc is null) return ResolveDueUtc - ResolveWithin * AtRiskFraction;
            return null;
        }
    }

    // The ticket was created: both targets start running. The time is the ticket's own, never the handler's "now".
    public static SlaClock Start(TicketId ticketId, Priority priority, DateTime createdUtc, TimeSpan respondWithin, TimeSpan resolveWithin)
    {
        createdUtc = WholeMicroseconds(createdUtc);
        var clock = new SlaClock
        {
            Id = ticketId,
            Priority = priority,
            StartedUtc = createdUtc,
            RespondWithin = respondWithin,
            ResolveWithin = resolveWithin,
            ResponseDueUtc = createdUtc + respondWithin,
            ResolveDueUtc = createdUtc + resolveWithin,
        };
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
    public void ChangeTargets(Priority priority, DateTime atUtc, TimeSpan respondWithin, TimeSpan resolveWithin)
    {
        atUtc = WholeMicroseconds(atUtc);
        if (Cancelled) return;
        if (PriorityChangedUtc is not null && atUtc <= PriorityChangedUtc) return;

        PriorityChangedUtc = atUtc;
        var before = (Priority, RespondWithin, ResolveWithin);

        Priority = priority;
        if (RespondedUtc is null)
        {
            RespondWithin = respondWithin;
            ResponseDueUtc = StartedUtc + respondWithin;
        }
        if (ResolvedUtc is null)
        {
            ResolveWithin = resolveWithin;
            ResolveDueUtc = StartedUtc + resolveWithin;
        }

        var after = (Priority, RespondWithin, ResolveWithin);
        if (before == after) return;

        Changed();
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
