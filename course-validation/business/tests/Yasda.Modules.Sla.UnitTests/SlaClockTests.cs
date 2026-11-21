using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Yasda.Modules.Sla.Domain;

namespace Yasda.Modules.Sla.UnitTests;

// One ticket's two deadlines. Every method must give the right answer when its event arrives twice, or after a later one.
public class SlaClockTests
{
    private static readonly TicketId Ticket = new(7);
    private static readonly TimeSpan Respond = TimeSpan.FromHours(4);
    private static readonly TimeSpan Resolve = TimeSpan.FromHours(24);

    // Open every hour of every day (to the last tick before midnight), so these tests can count time like a wall clock. The business
    // hours themselves are tested in BusinessCalendarTests and at the end of this file.
    private static readonly BusinessCalendar AllDay = AlwaysOpen();

    private static BusinessCalendar AlwaysOpen()
    {
        var week = new Dictionary<DayOfWeek, OpenHours>();
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            week[day] = new OpenHours(new TimeOnly(0, 0), TimeOnly.MaxValue);
        }
        return new BusinessCalendar(TimeZoneInfo.Utc, week, []);
    }

    // The ticket is created at 09:00; the test moves this clock to say when the other things happen.
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    private SlaClock NewClock()
    {
        var clock = SlaClock.Start(Ticket, Priority.High, Now, Respond, Resolve, AllDay);
        clock.ClearEvents();
        return clock;
    }

    private static DeadlinesChangedEvent OnlyEventOf(SlaClock clock)
    {
        clock.DomainEvents.Should().ContainSingle();
        return (DeadlinesChangedEvent)clock.DomainEvents[0];
    }

    [Fact]
    public void Starting_sets_both_due_times_from_the_ticket_time_and_the_targets()
    {
        var clock = SlaClock.Start(Ticket, Priority.High, Now, Respond, Resolve, AllDay);

        clock.ResponseDueUtc.Should().Be(Now.AddHours(4));
        clock.ResolveDueUtc.Should().Be(Now.AddHours(24));
        clock.ResponseMet.Should().BeNull();
        clock.ResolutionMet.Should().BeNull();
    }

    [Fact]
    public void Starting_raises_version_one_with_the_response_as_the_next_due_time()
    {
        var clock = SlaClock.Start(Ticket, Priority.High, Now, Respond, Resolve, AllDay);

        var changed = OnlyEventOf(clock);

        changed.Version.Should().Be(1);
        changed.TicketId.Should().Be(Ticket);
        changed.NextDueUtc.Should().Be(Now.AddHours(4));
    }

    [Fact]
    public void A_response_before_the_due_time_meets_the_target_and_the_resolve_target_is_next()
    {
        var clock = NewClock();

        clock.RecordResponse(Now.AddHours(1), AllDay);

        clock.ResponseMet.Should().BeTrue();
        var changed = OnlyEventOf(clock);
        changed.Version.Should().Be(2);
        changed.ResponseMet.Should().BeTrue();
        changed.NextDueUtc.Should().Be(Now.AddHours(24));
    }

    [Fact]
    public void A_response_exactly_at_the_due_minute_meets_the_target()
    {
        var clock = NewClock();

        clock.RecordResponse(Now.AddHours(4), AllDay);

        clock.ResponseMet.Should().BeTrue();
    }

    [Fact]
    public void A_response_a_minute_late_misses_the_target()
    {
        var clock = NewClock();

        clock.RecordResponse(Now.AddHours(4).AddMinutes(1), AllDay);

        clock.ResponseMet.Should().BeFalse();
    }

    [Fact]
    public void A_second_response_changes_nothing()
    {
        var clock = NewClock();
        clock.RecordResponse(Now.AddHours(1), AllDay);
        clock.ClearEvents();

        clock.RecordResponse(Now.AddHours(3), AllDay);

        clock.RespondedUtc.Should().Be(Now.AddHours(1));
        clock.Version.Should().Be(2);
        clock.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void A_resolution_exactly_at_the_due_minute_meets_the_target_and_a_minute_later_misses_it()
    {
        var onTime = NewClock();
        var late = NewClock();

        onTime.RecordResolution(Now.AddHours(24), AllDay);
        late.RecordResolution(Now.AddHours(24).AddMinutes(1), AllDay);

        onTime.ResolutionMet.Should().BeTrue();
        late.ResolutionMet.Should().BeFalse();
    }

    [Fact]
    public void Resolving_after_the_response_leaves_nothing_running()
    {
        var clock = NewClock();
        clock.RecordResponse(Now.AddHours(1), AllDay);

        clock.RecordResolution(Now.AddHours(5), AllDay);

        clock.NextDueUtc.Should().BeNull();
        clock.AtRiskFromUtc.Should().BeNull();
    }

    [Fact]
    public void Reopening_runs_the_resolve_target_again_with_the_same_due_time()
    {
        var clock = NewClock();
        clock.RecordResolution(Now.AddHours(5), AllDay);
        clock.ClearEvents();

        clock.Reopen(Now.AddHours(6));

        clock.ResolvedUtc.Should().BeNull();
        clock.ResolutionMet.Should().BeNull();
        clock.ResolveDueUtc.Should().Be(Now.AddHours(24));
        OnlyEventOf(clock).ResolutionMet.Should().BeNull();
    }

    [Fact]
    public void Resolve_and_reopen_in_reverse_order_leave_the_resolve_target_running()
    {
        var clock = NewClock();

        clock.Reopen(Now.AddHours(6));
        clock.RecordResolution(Now.AddHours(5), AllDay);

        clock.ResolvedUtc.Should().BeNull();
        clock.ResolutionMet.Should().BeNull();
    }

    [Fact]
    public void A_repeated_resolution_or_reopen_changes_nothing()
    {
        var clock = NewClock();
        clock.RecordResolution(Now.AddHours(5), AllDay);
        clock.Reopen(Now.AddHours(6));
        clock.ClearEvents();
        var version = clock.Version;

        clock.RecordResolution(Now.AddHours(5), AllDay);
        clock.Reopen(Now.AddHours(6));

        clock.Version.Should().Be(version);
        clock.DomainEvents.Should().BeEmpty();
        clock.ResolvedUtc.Should().BeNull();
    }

    [Fact]
    public void A_resolution_that_arrives_after_a_later_resolution_is_ignored()
    {
        var clock = NewClock();
        clock.RecordResolution(Now.AddHours(5), AllDay);
        clock.Reopen(Now.AddHours(6));
        clock.RecordResolution(Now.AddHours(7), AllDay);

        clock.Reopen(Now.AddHours(6));

        clock.ResolvedUtc.Should().Be(Now.AddHours(7));
    }

    [Fact]
    public void Cancelling_stops_both_targets_and_measures_neither()
    {
        var clock = NewClock();

        clock.Cancel();

        clock.ResponseMet.Should().BeNull();
        clock.ResolutionMet.Should().BeNull();
        clock.NextDueUtc.Should().BeNull();
        var changed = OnlyEventOf(clock);
        changed.NextDueUtc.Should().BeNull();
        changed.AtRiskFromUtc.Should().BeNull();
    }

    [Fact]
    public void A_cancelled_clock_ignores_every_event_that_arrives_after_it()
    {
        var clock = NewClock();
        clock.Cancel();
        clock.ClearEvents();

        clock.RecordResponse(Now.AddHours(1), AllDay);
        clock.RecordResolution(Now.AddHours(2), AllDay);
        clock.Reopen(Now.AddHours(3));
        clock.ChangeTargets(Priority.Critical, Now.AddHours(4), TimeSpan.FromHours(1), TimeSpan.FromHours(4), AllDay);
        clock.Cancel();

        clock.DomainEvents.Should().BeEmpty();
        clock.RespondedUtc.Should().BeNull();
        clock.ResolvedUtc.Should().BeNull();
    }

    [Fact]
    public void A_priority_change_recomputes_the_running_targets_from_the_start()
    {
        var clock = NewClock();

        clock.ChangeTargets(Priority.Critical, Now.AddMinutes(30), TimeSpan.FromHours(1), TimeSpan.FromHours(4), AllDay);

        clock.Priority.Should().Be(Priority.Critical);
        clock.ResponseDueUtc.Should().Be(Now.AddHours(1));
        clock.ResolveDueUtc.Should().Be(Now.AddHours(4));
        OnlyEventOf(clock).NextDueUtc.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void A_priority_change_leaves_a_finished_target_alone()
    {
        var clock = NewClock();
        clock.RecordResponse(Now.AddHours(1), AllDay);

        clock.ChangeTargets(Priority.Low, Now.AddHours(2), TimeSpan.FromHours(24), TimeSpan.FromHours(120), AllDay);

        clock.ResponseDueUtc.Should().Be(Now.AddHours(4));
        clock.ResponseMet.Should().BeTrue();
        clock.ResolveDueUtc.Should().Be(Now.AddHours(120));
    }

    [Fact]
    public void Two_priority_changes_in_reverse_order_keep_the_newer()
    {
        var clock = NewClock();

        clock.ChangeTargets(Priority.Critical, Now.AddHours(2), TimeSpan.FromHours(1), TimeSpan.FromHours(4), AllDay);
        clock.ChangeTargets(Priority.Low, Now.AddHours(1), TimeSpan.FromHours(24), TimeSpan.FromHours(120), AllDay);

        clock.Priority.Should().Be(Priority.Critical);
        clock.ResponseDueUtc.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void A_priority_change_to_the_same_numbers_changes_nothing()
    {
        var clock = NewClock();

        clock.ChangeTargets(Priority.High, Now.AddHours(1), Respond, Resolve, AllDay);

        clock.DomainEvents.Should().BeEmpty();
        clock.Version.Should().Be(1);
    }

    // PostgreSQL keeps whole microseconds, and the event's time has finer ticks. A clock loaded from the database has a
    // due time cut to the microsecond, so a reply made exactly at the due time must still count as on time.
    [Fact]
    public void A_response_at_the_due_time_is_on_time_even_when_the_event_time_has_finer_ticks_than_the_database_keeps()
    {
        var startedInDatabase = Now;
        var clock = SlaClock.Start(Ticket, Priority.High, startedInDatabase, Respond, Resolve, AllDay);
        var eventTime = startedInDatabase.AddHours(4).AddTicks(8);

        clock.RecordResponse(eventTime, AllDay);

        clock.ResponseMet.Should().BeTrue();
    }

    [Fact]
    public void Resolving_a_ticket_nobody_replied_to_leaves_the_response_target_running()
    {
        var clock = NewClock();

        clock.RecordResolution(Now.AddHours(1), AllDay);

        clock.ResolutionMet.Should().BeTrue();
        clock.ResponseMet.Should().BeNull();
        clock.RespondedUtc.Should().BeNull();
        clock.NextDueUtc.Should().Be(Now.AddHours(4), "only a public reply from an agent stops the response target");
    }

    [Fact]
    public void The_at_risk_point_is_a_quarter_of_the_target_before_the_response_is_due()
    {
        var clock = NewClock();

        // A 4 hour target is at risk for its last hour.
        clock.AtRiskFromUtc.Should().Be(Now.AddHours(3));
    }

    [Fact]
    public void After_the_response_the_at_risk_point_belongs_to_the_resolve_target()
    {
        var clock = NewClock();

        clock.RecordResponse(Now.AddHours(1), AllDay);

        // A 24 hour target is at risk for its last 6 hours.
        clock.AtRiskFromUtc.Should().Be(Now.AddHours(18));
    }

    // Monday to Friday 08:00-17:00 in Manila. The ticket is made on Friday 2026-11-20 at 16:00 Manila time.
    private static readonly TimeZoneInfo Manila = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");

    private static BusinessCalendar OfficeWeek(params DateOnly[] holidays)
    {
        var hours = new OpenHours(new TimeOnly(8, 0), new TimeOnly(17, 0));
        var week = new Dictionary<DayOfWeek, OpenHours>
        {
            [DayOfWeek.Monday] = hours, [DayOfWeek.Tuesday] = hours, [DayOfWeek.Wednesday] = hours,
            [DayOfWeek.Thursday] = hours, [DayOfWeek.Friday] = hours,
        };
        return new BusinessCalendar(Manila, week, holidays);
    }

    private static DateTime ManilaTime(int month, int day, int hour, int minute = 0)
    {
        return TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, month, day, hour, minute, 0, DateTimeKind.Unspecified), Manila);
    }

    [Fact]
    public void Deadlines_count_open_hours_only_so_a_friday_afternoon_ticket_is_due_on_monday()
    {
        var friday4pm = ManilaTime(11, 20, 16);

        var clock = SlaClock.Start(Ticket, Priority.High, friday4pm, TimeSpan.FromHours(4), TimeSpan.FromHours(24), OfficeWeek());

        // One hour on Friday, three on Monday morning. 24 open hours: 1 on Friday, then Monday, Tuesday (9 each), Wednesday 5.
        clock.ResponseDueUtc.Should().Be(ManilaTime(11, 23, 11));
        clock.ResolveDueUtc.Should().Be(ManilaTime(11, 25, 13));
    }

    [Fact]
    public void The_at_risk_point_is_three_quarters_through_the_open_time_not_the_wall_clock_time()
    {
        var friday4pm = ManilaTime(11, 20, 16);

        var clock = SlaClock.Start(Ticket, Priority.High, friday4pm, TimeSpan.FromHours(4), TimeSpan.FromHours(24), OfficeWeek());

        // 3 open hours after Friday 16:00 is Monday 10:00: the last open hour (10:00 to 11:00) is the at-risk hour.
        clock.AtRiskFromUtc.Should().Be(ManilaTime(11, 23, 10));
    }

    [Fact]
    public void A_ticket_made_on_a_holiday_starts_counting_at_the_next_opening()
    {
        var holidayNoon = ManilaTime(11, 30, 12);

        var clock = SlaClock.Start(Ticket, Priority.High, holidayNoon, TimeSpan.FromHours(4), TimeSpan.FromHours(24), OfficeWeek(new DateOnly(2026, 11, 30)));

        clock.ResponseDueUtc.Should().Be(ManilaTime(12, 1, 12));
    }

    [Fact]
    public void Changing_the_calendar_moves_the_running_deadlines_and_tells_everyone_once()
    {
        var friday4pm = ManilaTime(11, 20, 16);
        var clock = SlaClock.Start(Ticket, Priority.High, friday4pm, TimeSpan.FromHours(4), TimeSpan.FromHours(24), OfficeWeek());
        clock.ClearEvents();

        // The 23rd becomes a holiday: the response that was due Monday 11:00 is now due Tuesday 11:00.
        clock.Recalculate(TimeSpan.FromHours(4), TimeSpan.FromHours(24), OfficeWeek(new DateOnly(2026, 11, 23)));

        clock.ResponseDueUtc.Should().Be(ManilaTime(11, 24, 11));
        var changed = OnlyEventOf(clock);
        changed.Version.Should().Be(2);
        changed.ResponseDueUtc.Should().Be(ManilaTime(11, 24, 11));
    }

    [Fact]
    public void Recalculating_under_the_same_rules_changes_nothing()
    {
        var friday4pm = ManilaTime(11, 20, 16);
        var clock = SlaClock.Start(Ticket, Priority.High, friday4pm, TimeSpan.FromHours(4), TimeSpan.FromHours(24), OfficeWeek());
        clock.ClearEvents();

        clock.Recalculate(TimeSpan.FromHours(4), TimeSpan.FromHours(24), OfficeWeek());

        clock.DomainEvents.Should().BeEmpty();
        clock.Version.Should().Be(1);
    }

    [Fact]
    public void Recalculating_leaves_a_finished_target_with_its_due_time_and_its_numbers()
    {
        var friday4pm = ManilaTime(11, 20, 16);
        var clock = SlaClock.Start(Ticket, Priority.High, friday4pm, TimeSpan.FromHours(4), TimeSpan.FromHours(24), OfficeWeek());
        clock.RecordResponse(ManilaTime(11, 23, 9), AllDay);
        var dueBefore = clock.ResponseDueUtc;

        clock.Recalculate(TimeSpan.FromHours(1), TimeSpan.FromHours(8), OfficeWeek(new DateOnly(2026, 11, 23)));

        clock.ResponseDueUtc.Should().Be(dueBefore);
        clock.RespondWithin.Should().Be(TimeSpan.FromHours(4));
        clock.ResolveWithin.Should().Be(TimeSpan.FromHours(8), "the resolve target was still running, so it takes the new number");
        clock.ResponseMet.Should().BeTrue();
    }

    [Fact]
    public void A_cancelled_clock_ignores_a_recalculation()
    {
        var clock = NewClock();
        clock.Cancel();
        clock.ClearEvents();

        clock.Recalculate(TimeSpan.FromHours(1), TimeSpan.FromHours(2), AllDay);

        clock.DomainEvents.Should().BeEmpty();
        clock.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void A_clock_is_running_until_both_targets_are_finished()
    {
        var clock = NewClock();
        clock.IsRunning.Should().BeTrue();

        clock.RecordResponse(Now.AddHours(1), AllDay);
        clock.IsRunning.Should().BeTrue();

        clock.RecordResolution(Now.AddHours(2), AllDay);
        clock.IsRunning.Should().BeFalse();
    }

    // ---- Pause and resume: a ticket waiting on the requester stops the clock. Mon 2026-11-23 09:00 Manila, 4 hours to respond.

    private static SlaClock MondayMorningClock()
    {
        var clock = SlaClock.Start(Ticket, Priority.High, ManilaTime(11, 23, 9), TimeSpan.FromHours(4), TimeSpan.FromHours(24), OfficeWeek());
        clock.ClearEvents();
        return clock;
    }

    [Fact]
    public void Pausing_stops_the_clock_so_nothing_is_due_or_at_risk_and_the_event_says_paused()
    {
        var clock = MondayMorningClock();

        clock.Pause(ManilaTime(11, 23, 10));

        clock.IsPaused.Should().BeTrue();
        clock.NextDueUtc.Should().BeNull();
        clock.AtRiskFromUtc.Should().BeNull();
        var changed = OnlyEventOf(clock);
        changed.Paused.Should().BeTrue();
        changed.NextDueUtc.Should().BeNull();
    }

    [Fact]
    public void Resuming_adds_the_open_time_it_was_paused_to_the_deadlines()
    {
        var clock = MondayMorningClock();
        clock.Pause(ManilaTime(11, 23, 10));
        clock.ClearEvents();

        // Paused Monday 10:00 to Tuesday 10:00: 7 open hours on Monday and 2 on Tuesday.
        clock.Resume(ManilaTime(11, 24, 10), OfficeWeek());

        clock.IsPaused.Should().BeFalse();
        clock.PausedFor.Should().Be(TimeSpan.FromHours(9));
        clock.ResponseDueUtc.Should().Be(ManilaTime(11, 24, 13), "4 open hours after 09:00 was 13:00 Monday; 9 paused hours later it is 13:00 Tuesday");
        var changed = OnlyEventOf(clock);
        changed.Paused.Should().BeFalse();
        changed.NextDueUtc.Should().Be(ManilaTime(11, 24, 13));
    }

    [Fact]
    public void A_pause_over_a_weekend_costs_nothing_for_the_closed_days()
    {
        var clock = MondayMorningClock();
        clock.Pause(ManilaTime(11, 27, 16));

        // Friday 16:00 to the next Monday 09:00: one open hour on Friday and one on Monday.
        clock.Resume(ManilaTime(11, 30, 9), OfficeWeek());

        clock.PausedFor.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void A_repeated_pause_or_resume_changes_nothing()
    {
        var clock = MondayMorningClock();
        clock.Pause(ManilaTime(11, 23, 10));
        clock.Resume(ManilaTime(11, 23, 11), OfficeWeek());
        clock.ClearEvents();
        var version = clock.Version;

        clock.Pause(ManilaTime(11, 23, 10));
        clock.Resume(ManilaTime(11, 23, 11), OfficeWeek());
        clock.Resume(ManilaTime(11, 23, 12), OfficeWeek());

        clock.Version.Should().Be(version);
        clock.DomainEvents.Should().BeEmpty();
        clock.PausedFor.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void A_resume_that_arrives_before_its_pause_leaves_the_clock_running()
    {
        var clock = MondayMorningClock();

        clock.Resume(ManilaTime(11, 23, 11), OfficeWeek());
        clock.Pause(ManilaTime(11, 23, 10));

        clock.IsPaused.Should().BeFalse();
        clock.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Pausing_a_clock_with_nothing_left_running_or_a_cancelled_one_changes_nothing()
    {
        var finished = MondayMorningClock();
        finished.RecordResponse(ManilaTime(11, 23, 10), OfficeWeek());
        finished.RecordResolution(ManilaTime(11, 23, 11), OfficeWeek());
        var cancelled = MondayMorningClock();
        cancelled.Cancel();
        finished.ClearEvents();
        cancelled.ClearEvents();

        finished.Pause(ManilaTime(11, 23, 12));
        cancelled.Pause(ManilaTime(11, 23, 12));

        finished.IsPaused.Should().BeFalse();
        cancelled.IsPaused.Should().BeFalse();
        finished.DomainEvents.Should().BeEmpty();
        cancelled.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Resolving_while_paused_ends_the_pause_and_counts_the_paused_time_so_the_resolution_is_on_time()
    {
        var clock = MondayMorningClock();
        clock.Pause(ManilaTime(11, 23, 10));

        // The resolve target is 24 open hours (due Wednesday 15:00 without a pause). Resolved Wednesday 16:00 after a long pause.
        clock.RecordResolution(ManilaTime(11, 25, 16), OfficeWeek());

        clock.IsPaused.Should().BeFalse();
        clock.ResolutionMet.Should().BeTrue();
    }

    [Fact]
    public void A_response_given_while_paused_is_measured_against_a_due_time_that_includes_the_pause_so_far()
    {
        var clock = MondayMorningClock();
        clock.Pause(ManilaTime(11, 23, 10));

        // Due Monday 13:00 without a pause. The agent answers Tuesday 09:00 while the ticket still waits.
        clock.RecordResponse(ManilaTime(11, 24, 9), OfficeWeek());

        clock.ResponseMet.Should().BeTrue();
        clock.IsPaused.Should().BeTrue("answering does not end the wait");
    }

    [Fact]
    public void Changing_the_calendar_while_paused_keeps_the_clock_paused()
    {
        var clock = MondayMorningClock();
        clock.Pause(ManilaTime(11, 23, 10));
        clock.ClearEvents();

        clock.Recalculate(TimeSpan.FromHours(4), TimeSpan.FromHours(24), OfficeWeek(new DateOnly(2026, 11, 25)));

        clock.IsPaused.Should().BeTrue();
        clock.NextDueUtc.Should().BeNull();
    }

    // ---- The scheduler's questions. Monday 09:00 Manila, 4 open hours to respond (due 13:00, at risk from 12:00), 24 to resolve.

    [Fact]
    public void A_new_clock_is_next_checked_when_its_response_target_enters_its_last_quarter()
    {
        var clock = MondayMorningClock();

        clock.NextCheckUtc.Should().Be(ManilaTime(11, 23, 12));
    }

    [Fact]
    public void Scanning_before_the_last_quarter_raises_nothing()
    {
        var clock = MondayMorningClock();

        clock.Scan(ManilaTime(11, 23, 11, 59));

        clock.DomainEvents.Should().BeEmpty();
        clock.NextCheckUtc.Should().Be(ManilaTime(11, 23, 12));
    }

    [Fact]
    public void Scanning_in_the_last_quarter_raises_one_warning_and_then_waits_for_the_due_time()
    {
        var clock = MondayMorningClock();

        clock.Scan(ManilaTime(11, 23, 12, 30));
        clock.Scan(ManilaTime(11, 23, 12, 31));

        var warning = clock.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<SlaBreachImminentEvent>().Which;
        warning.Target.Should().Be(SlaTarget.Response);
        warning.DueUtc.Should().Be(ManilaTime(11, 23, 13));
        clock.NextCheckUtc.Should().Be(ManilaTime(11, 23, 13));
    }

    [Fact]
    public void Scanning_after_the_due_time_raises_one_breach_and_then_checks_the_resolve_target()
    {
        var clock = MondayMorningClock();
        clock.Scan(ManilaTime(11, 23, 12, 30));
        clock.ClearEvents();

        clock.Scan(ManilaTime(11, 23, 13, 1));
        clock.Scan(ManilaTime(11, 23, 14));

        clock.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<SlaBreachedEvent>();
        clock.ResponseBreachedUtc.Should().Be(ManilaTime(11, 23, 13, 1));
        clock.NextCheckUtc.Should().Be(clock.ResolveAtRiskUtc, "the response target is done with; the resolve target is next");
    }

    [Fact]
    public void A_target_found_already_late_is_breached_without_a_warning_first()
    {
        var clock = MondayMorningClock();

        clock.Scan(ManilaTime(11, 24, 9));

        clock.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<SlaBreachedEvent>();
        clock.ResponseWarnedUtc.Should().BeNull();
    }

    [Fact]
    public void Scanning_exactly_at_the_due_time_is_not_yet_a_breach()
    {
        var clock = MondayMorningClock();
        clock.Scan(ManilaTime(11, 23, 12, 30));
        clock.ClearEvents();

        clock.Scan(ManilaTime(11, 23, 13));

        clock.DomainEvents.Should().BeEmpty("an answer at exactly the due time is still on time");
    }

    [Fact]
    public void A_met_target_is_never_announced_and_a_clock_with_nothing_running_has_no_check_time()
    {
        var clock = MondayMorningClock();
        clock.RecordResponse(ManilaTime(11, 23, 10), OfficeWeek());
        clock.RecordResolution(ManilaTime(11, 23, 11), OfficeWeek());
        clock.ClearEvents();

        clock.Scan(ManilaTime(12, 30, 9));

        clock.DomainEvents.Should().BeEmpty();
        clock.NextCheckUtc.Should().BeNull();
    }

    [Fact]
    public void A_paused_or_cancelled_clock_is_never_announced_and_has_no_check_time()
    {
        var paused = MondayMorningClock();
        paused.Pause(ManilaTime(11, 23, 10));
        var cancelled = MondayMorningClock();
        cancelled.Cancel();
        paused.ClearEvents();
        cancelled.ClearEvents();

        paused.Scan(ManilaTime(12, 30, 9));
        cancelled.Scan(ManilaTime(12, 30, 9));

        paused.DomainEvents.Should().BeEmpty();
        cancelled.DomainEvents.Should().BeEmpty();
        paused.NextCheckUtc.Should().BeNull();
        cancelled.NextCheckUtc.Should().BeNull();
    }

    [Fact]
    public void Resuming_makes_the_clock_checkable_again_at_its_new_times()
    {
        var clock = MondayMorningClock();
        clock.Pause(ManilaTime(11, 23, 10));

        clock.Resume(ManilaTime(11, 24, 10), OfficeWeek());

        clock.NextCheckUtc.Should().Be(clock.ResponseAtRiskUtc);
    }

    [Fact]
    public void A_warning_already_sent_is_not_sent_again_when_the_due_time_moves_later()
    {
        var clock = MondayMorningClock();
        clock.Scan(ManilaTime(11, 23, 12, 30));
        clock.ClearEvents();

        clock.Recalculate(TimeSpan.FromHours(8), TimeSpan.FromHours(24), OfficeWeek());
        clock.Scan(ManilaTime(11, 24, 9));

        clock.DomainEvents.OfType<SlaBreachImminentEvent>().Should().BeEmpty("it was warned once already");
    }
}
