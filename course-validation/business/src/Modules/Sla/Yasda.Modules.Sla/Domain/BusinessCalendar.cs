using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Sla.Domain;

// The hours one weekday is open: from Opens until Closes, in the calendar's own time zone.
internal sealed record OpenHours(TimeOnly Opens, TimeOnly Closes);

// When the organization is open, and the two questions every deadline needs: "what is this long from now, counting only open
// time?" (Add) and "how much open time lies between these two moments?" (Between). It has no database and no clock, so it is
// tested on its own. All times that come in and go out are UTC; the weekday hours and holidays are read in the calendar's zone.
//
// A deadline that lands exactly at closing time stays at closing time (the target is met if the answer comes at that minute).
// Time outside open hours never counts: Add from a Sunday starts counting at Monday's opening.
internal sealed class BusinessCalendar
{
    // A calendar with no open day would make Add search forever. Ten years of days is far more than any real gap.
    private const int MostDaysToSearch = 3650;

    private readonly TimeZoneInfo _zone;
    private readonly Dictionary<DayOfWeek, OpenHours> _week;
    private readonly HashSet<DateOnly> _holidays;

    public BusinessCalendar(TimeZoneInfo zone, IReadOnlyDictionary<DayOfWeek, OpenHours> week, IEnumerable<DateOnly> holidays)
    {
        if (week.Count == 0) throw new DomainException(SlaErrors.CalendarHasNoOpenDay);
        foreach (var hours in week.Values)
        {
            if (hours.Closes <= hours.Opens) throw new DomainException(SlaErrors.ClosingBeforeOpening);
        }

        _zone = zone;
        _week = new Dictionary<DayOfWeek, OpenHours>(week);
        _holidays = new HashSet<DateOnly>(holidays);
    }

    // The moment that is `duration` of open time after `startUtc`. Zero open time from a closed moment is the next opening.
    public DateTime Add(DateTime startUtc, TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration), "A duration can't be negative.");

        var left = duration;
        var date = LocalDate(startUtc);

        for (var day = 0; day < MostDaysToSearch; day++)
        {
            var open = OpenPeriod(date);
            date = date.AddDays(1);
            if (open is null) continue;

            var from = open.Value.StartUtc;
            if (startUtc > from) from = startUtc;
            var until = open.Value.EndUtc;
            if (from >= until) continue;

            var available = until - from;
            if (left <= available) return from + left;
            left = left - available;
        }

        throw new InvalidOperationException("No open time was found in ten years. The calendar must have at least one open day.");
    }

    // The open time between two moments. Zero when the second is not after the first.
    public TimeSpan Between(DateTime fromUtc, DateTime toUtc)
    {
        if (toUtc <= fromUtc) return TimeSpan.Zero;

        var total = TimeSpan.Zero;
        var date = LocalDate(fromUtc);
        var lastDate = LocalDate(toUtc);

        while (date <= lastDate)
        {
            var open = OpenPeriod(date);
            date = date.AddDays(1);
            if (open is null) continue;

            var from = open.Value.StartUtc;
            if (fromUtc > from) from = fromUtc;
            var until = open.Value.EndUtc;
            if (toUtc < until) until = toUtc;
            if (from >= until) continue;

            total = total + (until - from);
        }

        return total;
    }

    // Is the organization open at this moment? Closing time counts as open, so a deadline there is still on time.
    public bool IsOpenAt(DateTime utc)
    {
        var open = OpenPeriod(LocalDate(utc));
        if (open is null) return false;
        return utc >= open.Value.StartUtc && utc <= open.Value.EndUtc;
    }

    private DateOnly LocalDate(DateTime utc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zone);
        return DateOnly.FromDateTime(local);
    }

    // The open period of one local date, in UTC; null on a holiday and on a day with no hours.
    private (DateTime StartUtc, DateTime EndUtc)? OpenPeriod(DateOnly date)
    {
        if (_holidays.Contains(date)) return null;
        if (!_week.TryGetValue(date.DayOfWeek, out var hours)) return null;

        return (ToUtc(date, hours.Opens), ToUtc(date, hours.Closes));
    }

    // A local time that doesn't exist (the hour a clock skips in spring) is moved to the hour after it. A time that happens twice
    // (the hour a clock repeats in autumn) is read the way .NET reads it, as standard time.
    private DateTime ToUtc(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (_zone.IsInvalidTime(local)) local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, _zone);
    }
}
