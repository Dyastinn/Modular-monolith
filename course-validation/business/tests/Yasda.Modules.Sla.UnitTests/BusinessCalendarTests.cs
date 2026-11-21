using AwesomeAssertions;
using Yasda.Modules.Sla.Domain;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Sla.UnitTests;

// Monday to Friday, 08:00 to 17:00 in Manila (UTC+8, no daylight saving), with Monday 2026-11-30 a holiday.
// 2026-11-20 is a Friday, 2026-11-23 the Monday after it.
public class BusinessCalendarTests
{
    private static readonly TimeZoneInfo Manila = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");

    private static BusinessCalendar OfficeWeek(params DateOnly[] holidays)
    {
        var hours = new OpenHours(new TimeOnly(8, 0), new TimeOnly(17, 0));
        var week = new Dictionary<DayOfWeek, OpenHours>
        {
            [DayOfWeek.Monday] = hours,
            [DayOfWeek.Tuesday] = hours,
            [DayOfWeek.Wednesday] = hours,
            [DayOfWeek.Thursday] = hours,
            [DayOfWeek.Friday] = hours,
        };
        return new BusinessCalendar(Manila, week, holidays);
    }

    // A Manila wall-clock time, as the UTC moment it is.
    private static DateTime Manila_(int month, int day, int hour, int minute = 0)
    {
        var local = new DateTime(2026, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, Manila);
    }

    [Fact]
    public void Inside_one_open_day_the_deadline_is_just_the_duration_later()
    {
        var due = OfficeWeek().Add(Manila_(11, 18, 9), TimeSpan.FromHours(4));

        due.Should().Be(Manila_(11, 18, 13));
    }

    [Fact]
    public void Time_after_closing_carries_over_to_the_next_morning()
    {
        // Wednesday 16:00 plus 4 hours: one hour today, three tomorrow.
        var due = OfficeWeek().Add(Manila_(11, 18, 16), TimeSpan.FromHours(4));

        due.Should().Be(Manila_(11, 19, 11));
    }

    [Fact]
    public void A_deadline_that_lands_exactly_at_closing_time_stays_at_closing_time()
    {
        var due = OfficeWeek().Add(Manila_(11, 18, 13), TimeSpan.FromHours(4));

        due.Should().Be(Manila_(11, 18, 17));
    }

    [Fact]
    public void The_weekend_is_skipped()
    {
        // Friday 16:00 plus 2 hours: one hour on Friday, one on Monday.
        var due = OfficeWeek().Add(Manila_(11, 20, 16), TimeSpan.FromHours(2));

        due.Should().Be(Manila_(11, 23, 9));
    }

    [Fact]
    public void A_holiday_is_skipped_like_a_weekend_day()
    {
        var calendar = OfficeWeek(new DateOnly(2026, 11, 30));

        // Friday 2026-11-27 at 16:00 plus 2 hours: Monday the 30th is a holiday, so the second hour is on Tuesday.
        var due = calendar.Add(Manila_(11, 27, 16), TimeSpan.FromHours(2));

        due.Should().Be(Manila_(12, 1, 9));
    }

    [Fact]
    public void Counting_starts_at_opening_when_the_clock_starts_while_closed()
    {
        // Saturday morning, Friday evening and Monday before opening all start at Monday 08:00 (or Friday's close is passed).
        var calendar = OfficeWeek();

        calendar.Add(Manila_(11, 21, 10), TimeSpan.FromHours(1)).Should().Be(Manila_(11, 23, 9));
        calendar.Add(Manila_(11, 20, 18), TimeSpan.FromHours(1)).Should().Be(Manila_(11, 23, 9));
        calendar.Add(Manila_(11, 23, 6), TimeSpan.FromHours(1)).Should().Be(Manila_(11, 23, 9));
    }

    [Fact]
    public void No_time_at_all_from_a_closed_moment_is_the_next_opening()
    {
        OfficeWeek().Add(Manila_(11, 21, 10), TimeSpan.Zero).Should().Be(Manila_(11, 23, 8));
    }

    [Fact]
    public void Between_counts_only_open_time()
    {
        // Friday 16:00 to Monday 10:00: one hour Friday, two hours Monday.
        var open = OfficeWeek().Between(Manila_(11, 20, 16), Manila_(11, 23, 10));

        open.Should().Be(TimeSpan.FromHours(3));
    }

    [Fact]
    public void Between_is_zero_when_the_second_moment_is_not_after_the_first()
    {
        var calendar = OfficeWeek();

        calendar.Between(Manila_(11, 18, 12), Manila_(11, 18, 12)).Should().Be(TimeSpan.Zero);
        calendar.Between(Manila_(11, 18, 12), Manila_(11, 18, 9)).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void A_day_with_its_own_hours_uses_them()
    {
        var week = new Dictionary<DayOfWeek, OpenHours>
        {
            [DayOfWeek.Friday] = new OpenHours(new TimeOnly(8, 0), new TimeOnly(12, 0)),
            [DayOfWeek.Monday] = new OpenHours(new TimeOnly(8, 0), new TimeOnly(17, 0)),
        };
        var calendar = new BusinessCalendar(Manila, week, []);

        // Friday closes at noon, so Friday 11:00 plus 2 hours ends on Monday at 09:00.
        calendar.Add(Manila_(11, 20, 11), TimeSpan.FromHours(2)).Should().Be(Manila_(11, 23, 9));
    }

    [Fact]
    public void IsOpenAt_says_whether_the_moment_is_inside_open_hours()
    {
        var calendar = OfficeWeek(new DateOnly(2026, 11, 30));

        calendar.IsOpenAt(Manila_(11, 18, 8)).Should().BeTrue("opening time is open");
        calendar.IsOpenAt(Manila_(11, 18, 17)).Should().BeTrue("closing time still counts");
        calendar.IsOpenAt(Manila_(11, 18, 17, 1)).Should().BeFalse();
        calendar.IsOpenAt(Manila_(11, 18, 7, 59)).Should().BeFalse();
        calendar.IsOpenAt(Manila_(11, 21, 10)).Should().BeFalse("Saturday");
        calendar.IsOpenAt(Manila_(11, 30, 10)).Should().BeFalse("a holiday");
    }

    [Fact]
    public void A_calendar_with_no_open_day_is_refused()
    {
        var act = () => new BusinessCalendar(Manila, new Dictionary<DayOfWeek, OpenHours>(), []);

        act.Should().Throw<DomainException>().Which.Error.Should().Be(SlaErrors.CalendarHasNoOpenDay);
    }

    [Fact]
    public void A_day_that_closes_when_it_opens_or_before_is_refused()
    {
        var sameTime = new Dictionary<DayOfWeek, OpenHours> { [DayOfWeek.Monday] = new OpenHours(new TimeOnly(8, 0), new TimeOnly(8, 0)) };
        var backwards = new Dictionary<DayOfWeek, OpenHours> { [DayOfWeek.Monday] = new OpenHours(new TimeOnly(17, 0), new TimeOnly(8, 0)) };

        var first = () => new BusinessCalendar(Manila, sameTime, []);
        var second = () => new BusinessCalendar(Manila, backwards, []);

        first.Should().Throw<DomainException>().Which.Error.Should().Be(SlaErrors.ClosingBeforeOpening);
        second.Should().Throw<DomainException>().Which.Error.Should().Be(SlaErrors.ClosingBeforeOpening);
    }

    [Fact]
    public void A_negative_duration_is_refused()
    {
        var act = () => OfficeWeek().Add(Manila_(11, 18, 9), TimeSpan.FromHours(-1));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // New York skips 02:00-03:00 on 2026-03-08. A calendar that opens at 02:30 that day opens at 03:30 instead, not never.
    [Fact]
    public void An_opening_time_the_clocks_skip_moves_to_the_hour_after()
    {
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var week = new Dictionary<DayOfWeek, OpenHours>();
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            week[day] = new OpenHours(new TimeOnly(2, 30), new TimeOnly(6, 0));
        }
        var calendar = new BusinessCalendar(newYork, week, []);

        var start = new DateTime(2026, 3, 8, 0, 0, 0, DateTimeKind.Utc);
        var due = calendar.Add(start, TimeSpan.FromHours(1));

        // 03:30 in New York is 07:30 UTC once daylight time has begun, and the hour after it is 08:30.
        due.Should().Be(new DateTime(2026, 3, 8, 8, 30, 0, DateTimeKind.Utc));
    }
}
