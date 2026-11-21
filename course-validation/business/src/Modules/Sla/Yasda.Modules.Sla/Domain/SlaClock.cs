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

    // The ticket's category, when it has one: a category may have its own policy. Kept so a changed policy can find the right clocks.
    public CategoryId? CategoryId { get; private set; }
    public DateTime? CategoryChangedUtc { get; private set; }
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

    // While the ticket waits on the requester no time counts. PausedSinceUtc is set during a pause; PausedFor adds up the open
    // time of the pauses already over, and every due time is the start plus the target plus PausedFor.
    public DateTime? PausedSinceUtc { get; private set; }
    public TimeSpan PausedFor { get; private set; }
    public DateTime? LastPausedUtc { get; private set; }
    public DateTime? LastResumedUtc { get; private set; }
    public bool IsPaused => PausedSinceUtc is not null;

    // Each warning and each breach is raised once per target and remembered, so a restart or a second scheduler never repeats it.
    public DateTime? ResponseWarnedUtc { get; private set; }
    public DateTime? ResponseBreachedUtc { get; private set; }
    public DateTime? ResolveWarnedUtc { get; private set; }
    public DateTime? ResolveBreachedUtc { get; private set; }

    // The next moment the scheduler has something to do for this clock: a warning to send or a breach to announce. null when
    // nothing is running, or while paused. The scheduler looks for clocks whose NextCheckUtc has come.
    public DateTime? NextCheckUtc { get; private set; }
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
            if (Cancelled || IsPaused) return null;
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
            if (Cancelled || IsPaused) return null;
            if (RespondedUtc is null) return ResponseAtRiskUtc;
            if (ResolvedUtc is null) return ResolveAtRiskUtc;
            return null;
        }
    }

    // The ticket was created: both targets start running. The time is the ticket's own, never the handler's "now".
    public static SlaClock Start(TicketId ticketId, Priority priority, CategoryId? categoryId, DateTime createdUtc, TimeSpan respondWithin,
        TimeSpan resolveWithin, BusinessCalendar calendar)
    {
        createdUtc = WholeMicroseconds(createdUtc);
        var clock = new SlaClock
        {
            Id = ticketId,
            Priority = priority,
            CategoryId = categoryId,
            StartedUtc = createdUtc,
        };
        clock.SetResponseTarget(respondWithin, calendar);
        clock.SetResolveTarget(resolveWithin, calendar);
        clock.Changed();
        return clock;
    }

    // The first answer to the requester. Only the first counts. An answer given during a pause is measured against a due time that
    // already includes the pause so far.
    public void RecordResponse(DateTime atUtc, BusinessCalendar calendar)
    {
        if (Cancelled) return;
        if (RespondedUtc is not null) return;

        atUtc = WholeMicroseconds(atUtc);
        if (PausedSinceUtc is not null && atUtc > PausedSinceUtc)
        {
            var pausedSoFar = PausedFor + calendar.Between(PausedSinceUtc.Value, atUtc);
            ResponseDueUtc = WholeMicroseconds(calendar.Add(StartedUtc, RespondWithin + pausedSoFar));
        }

        RespondedUtc = atUtc;
        Changed();
    }

    // The ticket was resolved. Resolving does not count as a response: only an agent's public reply stops the response target
    // (decision of 2026-11-20), so a ticket fixed with no reply keeps showing its unanswered target.
    // A resolution that happened before the last reopen is old news, and one already known is a repeat.
    // Resolving ends a pause: the time paused until now is added to the deadlines first, so it is measured fairly.
    public void RecordResolution(DateTime atUtc, BusinessCalendar calendar)
    {
        atUtc = WholeMicroseconds(atUtc);
        if (Cancelled) return;
        if (ResolvedUtc is not null) return;
        if (LastReopenedUtc is not null && atUtc < LastReopenedUtc) return;

        if (IsPaused) EndPause(atUtc, calendar);
        ResolvedUtc = atUtc;
        Changed();
    }

    // The ticket now waits on the requester: the clock stops. A repeat, or a pause that was already followed by a resume, changes
    // nothing. Needs no calendar: the paused time is added up when the pause ends.
    public void Pause(DateTime atUtc)
    {
        atUtc = WholeMicroseconds(atUtc);
        if (!IsRunning) return;
        if (LastPausedUtc is not null && atUtc <= LastPausedUtc) return;

        LastPausedUtc = atUtc;
        if (LastResumedUtc is not null && atUtc <= LastResumedUtc) return;
        if (IsPaused) return;

        PausedSinceUtc = atUtc;
        Changed();
    }

    // The ticket no longer waits: the open time it was paused is added to every running target, and the due times are worked
    // out again.
    public void Resume(DateTime atUtc, BusinessCalendar calendar)
    {
        atUtc = WholeMicroseconds(atUtc);
        if (Cancelled) return;
        if (LastResumedUtc is not null && atUtc <= LastResumedUtc) return;

        LastResumedUtc = atUtc;
        if (!IsPaused) return;
        if (atUtc < PausedSinceUtc) return;

        EndPause(atUtc, calendar);
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

    // The ticket's category changed: the running targets use the new category's policy (or the installation's), from the start. A target
    // that is already finished keeps its due time. An older change that arrives late is ignored.
    public void ChangeCategory(CategoryId? categoryId, DateTime atUtc, TimeSpan respondWithin, TimeSpan resolveWithin, BusinessCalendar calendar)
    {
        atUtc = WholeMicroseconds(atUtc);
        if (Cancelled) return;
        if (CategoryChangedUtc is not null && atUtc <= CategoryChangedUtc) return;

        CategoryChangedUtc = atUtc;
        var before = Snapshot();
        var categoryBefore = CategoryId;

        CategoryId = categoryId;
        if (RespondedUtc is null) SetResponseTarget(respondWithin, calendar);
        if (ResolvedUtc is null) SetResolveTarget(resolveWithin, calendar);

        if (before == Snapshot() && categoryBefore == categoryId) return;

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

    private (Priority, TimeSpan, DateTime, DateTime, TimeSpan, DateTime, DateTime, DateTime?) Snapshot()
    {
        return (Priority, RespondWithin, ResponseDueUtc, ResponseAtRiskUtc, ResolveWithin, ResolveDueUtc, ResolveAtRiskUtc, PausedSinceUtc);
    }

    private void SetResponseTarget(TimeSpan within, BusinessCalendar calendar)
    {
        RespondWithin = within;
        ResponseDueUtc = WholeMicroseconds(calendar.Add(StartedUtc, within + PausedFor));
        ResponseAtRiskUtc = WholeMicroseconds(calendar.Add(StartedUtc, within * AtRiskAfterFraction + PausedFor));
    }

    private void SetResolveTarget(TimeSpan within, BusinessCalendar calendar)
    {
        ResolveWithin = within;
        ResolveDueUtc = WholeMicroseconds(calendar.Add(StartedUtc, within + PausedFor));
        ResolveAtRiskUtc = WholeMicroseconds(calendar.Add(StartedUtc, within * AtRiskAfterFraction + PausedFor));
    }

    // Adds the open time of the pause that is ending, and works the running targets out again. The caller raises the event.
    private void EndPause(DateTime atUtc, BusinessCalendar calendar)
    {
        PausedFor = PausedFor + calendar.Between(PausedSinceUtc!.Value, atUtc);
        PausedSinceUtc = null;

        if (RespondedUtc is null) SetResponseTarget(RespondWithin, calendar);
        if (ResolvedUtc is null) SetResolveTarget(ResolveWithin, calendar);
    }

    // PostgreSQL keeps whole microseconds; a time with finer ticks would compare differently before and after the clock is
    // saved, and a reply made exactly at the due time would count as late. Every time that comes in is cut the same way.
    private static DateTime WholeMicroseconds(DateTime value)
    {
        const long TicksPerMicrosecond = 10;
        return new DateTime(value.Ticks - value.Ticks % TicksPerMicrosecond, value.Kind);
    }

    // The scheduler asks each clock that is due a check: has a target run out of time, or entered its last quarter? Warned and
    // breached are remembered, so asking again changes nothing. A target that is already past due is breached, not warned.
    public void Scan(DateTime nowUtc)
    {
        nowUtc = WholeMicroseconds(nowUtc);
        if (Cancelled || IsPaused) return;

        if (RespondedUtc is null)
        {
            if (ResponseBreachedUtc is null && nowUtc > ResponseDueUtc)
            {
                ResponseBreachedUtc = nowUtc;
                Raise(new SlaBreachedEvent(Id, SlaTarget.Response, ResponseDueUtc));
            }
            else if (ResponseBreachedUtc is null && ResponseWarnedUtc is null && nowUtc >= ResponseAtRiskUtc)
            {
                ResponseWarnedUtc = nowUtc;
                Raise(new SlaBreachImminentEvent(Id, SlaTarget.Response, ResponseDueUtc));
            }
        }

        if (ResolvedUtc is null)
        {
            if (ResolveBreachedUtc is null && nowUtc > ResolveDueUtc)
            {
                ResolveBreachedUtc = nowUtc;
                Raise(new SlaBreachedEvent(Id, SlaTarget.Resolve, ResolveDueUtc));
            }
            else if (ResolveBreachedUtc is null && ResolveWarnedUtc is null && nowUtc >= ResolveAtRiskUtc)
            {
                ResolveWarnedUtc = nowUtc;
                Raise(new SlaBreachImminentEvent(Id, SlaTarget.Resolve, ResolveDueUtc));
            }
        }

        UpdateNextCheck();
    }

    private void UpdateNextCheck()
    {
        NextCheckUtc = null;
        if (Cancelled || IsPaused) return;

        if (RespondedUtc is null) NextCheckUtc = Earliest(NextCheckUtc, NextActionOf(ResponseWarnedUtc, ResponseBreachedUtc, ResponseAtRiskUtc, ResponseDueUtc));
        if (ResolvedUtc is null) NextCheckUtc = Earliest(NextCheckUtc, NextActionOf(ResolveWarnedUtc, ResolveBreachedUtc, ResolveAtRiskUtc, ResolveDueUtc));
    }

    // For one running target: when the next warning or breach is due, or null when both have been sent.
    private static DateTime? NextActionOf(DateTime? warnedUtc, DateTime? breachedUtc, DateTime atRiskUtc, DateTime dueUtc)
    {
        if (breachedUtc is not null) return null;
        if (warnedUtc is null) return atRiskUtc;
        return dueUtc;
    }

    private static DateTime? Earliest(DateTime? first, DateTime? second)
    {
        if (first is null) return second;
        if (second is null) return first;
        if (first < second) return first;
        return second;
    }

    private void Changed()
    {
        UpdateNextCheck();
        Version++;
        Raise(new DeadlinesChangedEvent(Id, Version, ResponseDueUtc, ResponseMet, ResolveDueUtc, ResolutionMet,
            NextDueUtc, AtRiskFromUtc, IsPaused));
    }
}
