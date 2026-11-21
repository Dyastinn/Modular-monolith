using AwesomeAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Yasda.Modules.Sla.Domain;

namespace Yasda.Modules.Sla.UnitTests;

// The rules of business time, checked against thousands of generated moments and durations in two time zones: Manila (never
// changes its clocks) and New York (spring forward on 2026-03-08, fall back on 2026-11-01).
public class BusinessCalendarPropertyTests
{
    private static readonly DateTime FirstMoment = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const int MinutesInTwoYears = 2 * 365 * 24 * 60;
    private const int MostMinutesToAdd = 30 * 24 * 60;

    private static readonly DateOnly[] Holidays =
    [
        new(2026, 3, 9), new(2026, 11, 2), new(2026, 11, 26), new(2026, 12, 25), new(2027, 1, 1),
    ];

    // Monday to Friday 08:00-17:00, Saturday 09:00-12:00, Sunday closed.
    private static BusinessCalendar CalendarIn(string zoneId)
    {
        var weekday = new OpenHours(new TimeOnly(8, 0), new TimeOnly(17, 0));
        var week = new Dictionary<DayOfWeek, OpenHours>
        {
            [DayOfWeek.Monday] = weekday,
            [DayOfWeek.Tuesday] = weekday,
            [DayOfWeek.Wednesday] = weekday,
            [DayOfWeek.Thursday] = weekday,
            [DayOfWeek.Friday] = weekday,
            [DayOfWeek.Saturday] = new OpenHours(new TimeOnly(9, 0), new TimeOnly(12, 0)),
        };
        return new BusinessCalendar(TimeZoneInfo.FindSystemTimeZoneById(zoneId), week, Holidays);
    }

    private static DateTime Moment(int minutes)
    {
        return FirstMoment.AddMinutes(minutes);
    }

    private static Arbitrary<int> Minutes(int most)
    {
        return Gen.Choose(0, most).ToArbitrary();
    }

    private static Arbitrary<(int Start, int Length)> StartAndLength()
    {
        var pairs = from start in Gen.Choose(0, MinutesInTwoYears)
                    from length in Gen.Choose(0, MostMinutesToAdd)
                    select (start, length);
        return pairs.ToArbitrary();
    }

    private static readonly string[] Zones = ["Asia/Manila", "America/New_York"];

    [Fact]
    public void Add_never_lands_outside_open_hours_and_never_on_a_holiday()
    {
        foreach (var zone in Zones)
        {
            var calendar = CalendarIn(zone);
            var property = Prop.ForAll(StartAndLength(), pair =>
            {
                var due = calendar.Add(Moment(pair.Start), TimeSpan.FromMinutes(pair.Length));

                due.Should().BeAfter(Moment(pair.Start).AddTicks(-1));
                calendar.IsOpenAt(due).Should().BeTrue($"{due:O} in {zone} must be inside open hours");
            });
            property.QuickCheckThrowOnFailure();
        }
    }

    [Fact]
    public void Between_a_start_and_its_deadline_is_exactly_the_duration()
    {
        foreach (var zone in Zones)
        {
            var calendar = CalendarIn(zone);
            var property = Prop.ForAll(StartAndLength(), pair =>
            {
                var duration = TimeSpan.FromMinutes(pair.Length);
                var due = calendar.Add(Moment(pair.Start), duration);

                calendar.Between(Moment(pair.Start), due).Should().Be(duration, $"in {zone}");
            });
            property.QuickCheckThrowOnFailure();
        }
    }

    [Fact]
    public void A_longer_duration_never_gives_an_earlier_deadline_and_a_later_start_never_gives_an_earlier_one_either()
    {
        foreach (var zone in Zones)
        {
            var calendar = CalendarIn(zone);
            var triples = from start in Gen.Choose(0, MinutesInTwoYears)
                          from shorter in Gen.Choose(0, MostMinutesToAdd)
                          from extra in Gen.Choose(0, 5000)
                          select (start, shorter, extra);

            Prop.ForAll(triples.ToArbitrary(), t =>
            {
                var start = Moment(t.start);
                var shorter = TimeSpan.FromMinutes(t.shorter);
                var longer = TimeSpan.FromMinutes(t.shorter + t.extra);

                calendar.Add(start, longer).Should().BeOnOrAfter(calendar.Add(start, shorter));
                calendar.Add(Moment(t.start + t.extra), shorter).Should().BeOnOrAfter(calendar.Add(start, shorter));
            }).QuickCheckThrowOnFailure();
        }
    }

    [Fact]
    public void Open_time_adds_up_across_a_middle_moment_and_never_shrinks_when_the_range_grows()
    {
        foreach (var zone in Zones)
        {
            var calendar = CalendarIn(zone);
            var triples = from a in Gen.Choose(0, MinutesInTwoYears)
                          from firstGap in Gen.Choose(0, MostMinutesToAdd)
                          from secondGap in Gen.Choose(0, MostMinutesToAdd)
                          select (a, firstGap, secondGap);

            Prop.ForAll(triples.ToArbitrary(), t =>
            {
                var a = Moment(t.a);
                var b = Moment(t.a + t.firstGap);
                var c = Moment(t.a + t.firstGap + t.secondGap);

                calendar.Between(a, c).Should().Be(calendar.Between(a, b) + calendar.Between(b, c), $"in {zone}");
                calendar.Between(a, c).Should().BeGreaterThanOrEqualTo(calendar.Between(a, b));
            }).QuickCheckThrowOnFailure();
        }
    }

    [Fact]
    public void Time_inside_a_closed_stretch_is_worth_nothing()
    {
        foreach (var zone in Zones)
        {
            var calendar = CalendarIn(zone);

            Prop.ForAll(Minutes(MinutesInTwoYears), minutes =>
            {
                var start = Moment(minutes);
                if (calendar.IsOpenAt(start)) return;

                // A moment the organization is closed at, and one minute later if it is still closed: nothing was open between.
                var later = start.AddMinutes(1);
                if (calendar.IsOpenAt(later)) return;
                calendar.Between(start, later).Should().Be(TimeSpan.Zero, $"in {zone}");
            }).QuickCheckThrowOnFailure();
        }
    }
}
