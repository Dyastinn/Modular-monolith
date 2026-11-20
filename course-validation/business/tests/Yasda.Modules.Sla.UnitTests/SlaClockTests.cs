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

    // The ticket is created at 09:00; the test moves this clock to say when the other things happen.
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    private SlaClock NewClock()
    {
        var clock = SlaClock.Start(Ticket, Priority.High, Now, Respond, Resolve);
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
        var clock = SlaClock.Start(Ticket, Priority.High, Now, Respond, Resolve);

        clock.ResponseDueUtc.Should().Be(Now.AddHours(4));
        clock.ResolveDueUtc.Should().Be(Now.AddHours(24));
        clock.ResponseMet.Should().BeNull();
        clock.ResolutionMet.Should().BeNull();
    }

    [Fact]
    public void Starting_raises_version_one_with_the_response_as_the_next_due_time()
    {
        var clock = SlaClock.Start(Ticket, Priority.High, Now, Respond, Resolve);

        var changed = OnlyEventOf(clock);

        changed.Version.Should().Be(1);
        changed.TicketId.Should().Be(Ticket);
        changed.NextDueUtc.Should().Be(Now.AddHours(4));
    }

    [Fact]
    public void A_response_before_the_due_time_meets_the_target_and_the_resolve_target_is_next()
    {
        var clock = NewClock();

        clock.RecordResponse(Now.AddHours(1));

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

        clock.RecordResponse(Now.AddHours(4));

        clock.ResponseMet.Should().BeTrue();
    }

    [Fact]
    public void A_response_a_minute_late_misses_the_target()
    {
        var clock = NewClock();

        clock.RecordResponse(Now.AddHours(4).AddMinutes(1));

        clock.ResponseMet.Should().BeFalse();
    }

    [Fact]
    public void A_second_response_changes_nothing()
    {
        var clock = NewClock();
        clock.RecordResponse(Now.AddHours(1));
        clock.ClearEvents();

        clock.RecordResponse(Now.AddHours(3));

        clock.RespondedUtc.Should().Be(Now.AddHours(1));
        clock.Version.Should().Be(2);
        clock.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void A_resolution_exactly_at_the_due_minute_meets_the_target_and_a_minute_later_misses_it()
    {
        var onTime = NewClock();
        var late = NewClock();

        onTime.RecordResolution(Now.AddHours(24));
        late.RecordResolution(Now.AddHours(24).AddMinutes(1));

        onTime.ResolutionMet.Should().BeTrue();
        late.ResolutionMet.Should().BeFalse();
    }

    [Fact]
    public void Resolving_after_the_response_leaves_nothing_running()
    {
        var clock = NewClock();
        clock.RecordResponse(Now.AddHours(1));

        clock.RecordResolution(Now.AddHours(5));

        clock.NextDueUtc.Should().BeNull();
        clock.AtRiskFromUtc.Should().BeNull();
    }

    [Fact]
    public void Reopening_runs_the_resolve_target_again_with_the_same_due_time()
    {
        var clock = NewClock();
        clock.RecordResolution(Now.AddHours(5));
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
        clock.RecordResolution(Now.AddHours(5));

        clock.ResolvedUtc.Should().BeNull();
        clock.ResolutionMet.Should().BeNull();
    }

    [Fact]
    public void A_repeated_resolution_or_reopen_changes_nothing()
    {
        var clock = NewClock();
        clock.RecordResolution(Now.AddHours(5));
        clock.Reopen(Now.AddHours(6));
        clock.ClearEvents();
        var version = clock.Version;

        clock.RecordResolution(Now.AddHours(5));
        clock.Reopen(Now.AddHours(6));

        clock.Version.Should().Be(version);
        clock.DomainEvents.Should().BeEmpty();
        clock.ResolvedUtc.Should().BeNull();
    }

    [Fact]
    public void A_resolution_that_arrives_after_a_later_resolution_is_ignored()
    {
        var clock = NewClock();
        clock.RecordResolution(Now.AddHours(5));
        clock.Reopen(Now.AddHours(6));
        clock.RecordResolution(Now.AddHours(7));

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

        clock.RecordResponse(Now.AddHours(1));
        clock.RecordResolution(Now.AddHours(2));
        clock.Reopen(Now.AddHours(3));
        clock.ChangeTargets(Priority.Critical, Now.AddHours(4), TimeSpan.FromHours(1), TimeSpan.FromHours(4));
        clock.Cancel();

        clock.DomainEvents.Should().BeEmpty();
        clock.RespondedUtc.Should().BeNull();
        clock.ResolvedUtc.Should().BeNull();
    }

    [Fact]
    public void A_priority_change_recomputes_the_running_targets_from_the_start()
    {
        var clock = NewClock();

        clock.ChangeTargets(Priority.Critical, Now.AddMinutes(30), TimeSpan.FromHours(1), TimeSpan.FromHours(4));

        clock.Priority.Should().Be(Priority.Critical);
        clock.ResponseDueUtc.Should().Be(Now.AddHours(1));
        clock.ResolveDueUtc.Should().Be(Now.AddHours(4));
        OnlyEventOf(clock).NextDueUtc.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void A_priority_change_leaves_a_finished_target_alone()
    {
        var clock = NewClock();
        clock.RecordResponse(Now.AddHours(1));

        clock.ChangeTargets(Priority.Low, Now.AddHours(2), TimeSpan.FromHours(24), TimeSpan.FromHours(120));

        clock.ResponseDueUtc.Should().Be(Now.AddHours(4));
        clock.ResponseMet.Should().BeTrue();
        clock.ResolveDueUtc.Should().Be(Now.AddHours(120));
    }

    [Fact]
    public void Two_priority_changes_in_reverse_order_keep_the_newer()
    {
        var clock = NewClock();

        clock.ChangeTargets(Priority.Critical, Now.AddHours(2), TimeSpan.FromHours(1), TimeSpan.FromHours(4));
        clock.ChangeTargets(Priority.Low, Now.AddHours(1), TimeSpan.FromHours(24), TimeSpan.FromHours(120));

        clock.Priority.Should().Be(Priority.Critical);
        clock.ResponseDueUtc.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void A_priority_change_to_the_same_numbers_changes_nothing()
    {
        var clock = NewClock();

        clock.ChangeTargets(Priority.High, Now.AddHours(1), Respond, Resolve);

        clock.DomainEvents.Should().BeEmpty();
        clock.Version.Should().Be(1);
    }

    // PostgreSQL keeps whole microseconds, and the event's time has finer ticks. A clock loaded from the database has a
    // due time cut to the microsecond, so a reply made exactly at the due time must still count as on time.
    [Fact]
    public void A_response_at_the_due_time_is_on_time_even_when_the_event_time_has_finer_ticks_than_the_database_keeps()
    {
        var startedInDatabase = Now;
        var clock = SlaClock.Start(Ticket, Priority.High, startedInDatabase, Respond, Resolve);
        var eventTime = startedInDatabase.AddHours(4).AddTicks(8);

        clock.RecordResponse(eventTime);

        clock.ResponseMet.Should().BeTrue();
    }

    [Fact]
    public void Resolving_a_ticket_nobody_replied_to_leaves_the_response_target_running()
    {
        var clock = NewClock();

        clock.RecordResolution(Now.AddHours(1));

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

        clock.RecordResponse(Now.AddHours(1));

        // A 24 hour target is at risk for its last 6 hours.
        clock.AtRiskFromUtc.Should().Be(Now.AddHours(18));
    }
}
