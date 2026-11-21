using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Sla.Domain;

// One weekday's opening hours. A closed day has no times at all.
internal sealed class WorkDay
{
    private WorkDay() { }

    public WorkDay(DayOfWeek day, TimeOnly? opens, TimeOnly? closes)
    {
        if (opens is null != closes is null) throw new DomainException(SlaErrors.ClosingBeforeOpening);
        if (opens is not null && closes <= opens) throw new DomainException(SlaErrors.ClosingBeforeOpening);

        Day = day;
        Opens = opens;
        Closes = closes;
    }

    public DayOfWeek Day { get; private set; }
    public TimeOnly? Opens { get; private set; }
    public TimeOnly? Closes { get; private set; }
    public bool IsOpen => Opens is not null;

    internal void SetHours(TimeOnly? opens, TimeOnly? closes)
    {
        Opens = opens;
        Closes = closes;
    }
}

// A date the organization is closed, and why.
internal sealed class Holiday
{
    private Holiday() { }

    internal Holiday(DateOnly date, string name)
    {
        Date = date;
        Name = name;
    }

    public DateOnly Date { get; private set; }
    public string Name { get; private set; } = "";
}

// The organization's working week, time zone and holidays: the data every deadline is counted from. An Admin edits it on a
// screen. There is exactly one.
internal sealed class WorkCalendar : AggregateRoot<CalendarId>
{
    public const int HolidayNameMaxLength = 100;

    private readonly List<WorkDay> _days = [];
    private readonly List<Holiday> _holidays = [];

    private WorkCalendar() { }

    // An IANA name such as "Asia/Manila".
    public string TimeZoneId { get; private set; } = "";
    public DateTime UpdatedUtc { get; private set; }
    public UserId UpdatedById { get; private set; }
    public string UpdatedByName { get; private set; } = "";

    public IReadOnlyList<WorkDay> Days => _days;
    public IReadOnlyList<Holiday> Holidays => _holidays;

    // The real calendar is seeded by the migration; Create is how the domain makes one (and how the tests do).
    public static WorkCalendar Create(string timeZoneId, IReadOnlyList<WorkDay> days, UserId by, string byName, DateTime nowUtc)
    {
        var calendar = new WorkCalendar { Id = new CalendarId(1) };
        calendar.Change(timeZoneId, days, by, byName, nowUtc);
        return calendar;
    }

    // Sets the time zone and the hours of the days named. A day that isn't named keeps its hours.
    public void Change(string timeZoneId, IReadOnlyList<WorkDay> days, UserId by, string byName, DateTime nowUtc)
    {
        FindZone(timeZoneId);
        EnsureEachDayOnce(days);
        EnsureOneOpenDayAfter(days);

        foreach (var incoming in days)
        {
            var existing = FindDay(incoming.Day);
            if (existing is null) _days.Add(incoming);
            else existing.SetHours(incoming.Opens, incoming.Closes);
        }

        TimeZoneId = timeZoneId;
        Touch(by, byName, nowUtc);
    }

    public void AddHoliday(DateOnly date, string name, UserId by, string byName, DateTime nowUtc)
    {
        var text = CheckedHolidayName(name);
        if (FindHoliday(date) is not null) throw new DomainException(SlaErrors.HolidayAlreadyListed);

        _holidays.Add(new Holiday(date, text));
        Touch(by, byName, nowUtc);
    }

    // Adds many holidays at once (a country's list, or a file). A date that is already a holiday keeps its name, so loading
    // the same list twice changes nothing. Returns how many were new. If one holiday is refused, none are added.
    public int AddHolidays(IReadOnlyList<PresetHoliday> holidays, UserId by, string byName, DateTime nowUtc)
    {
        var added = new List<Holiday>();
        var datesSeen = new HashSet<DateOnly>();
        foreach (var holiday in _holidays)
        {
            datesSeen.Add(holiday.Date);
        }

        foreach (var incoming in holidays)
        {
            var name = CheckedHolidayName(incoming.Name);
            if (!datesSeen.Add(incoming.Date)) continue;
            added.Add(new Holiday(incoming.Date, name));
        }

        if (added.Count == 0) return 0;

        _holidays.AddRange(added);
        Touch(by, byName, nowUtc);
        return added.Count;
    }

    // Removing a date that isn't a holiday changes nothing.
    public void RemoveHoliday(DateOnly date, UserId by, string byName, DateTime nowUtc)
    {
        var holiday = FindHoliday(date);
        if (holiday is null) return;

        _holidays.Remove(holiday);
        Touch(by, byName, nowUtc);
    }

    // The pure calendar the deadline math runs on.
    public BusinessCalendar ToBusinessCalendar()
    {
        var week = new Dictionary<DayOfWeek, OpenHours>();
        foreach (var day in _days)
        {
            if (day.Opens is null || day.Closes is null) continue;
            week[day.Day] = new OpenHours(day.Opens.Value, day.Closes.Value);
        }

        var dates = _holidays.Select(h => h.Date);
        return new BusinessCalendar(FindZone(TimeZoneId), week, dates);
    }

    private static string CheckedHolidayName(string name)
    {
        var text = name?.Trim() ?? "";
        if (text.Length == 0 || text.Length > HolidayNameMaxLength) throw new DomainException(SlaErrors.HolidayNameRequired);
        return text;
    }

    private static TimeZoneInfo FindZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new DomainException(SlaErrors.UnknownTimeZone, $"'{timeZoneId}' is not a time zone this server knows.");
        }
    }

    private static void EnsureEachDayOnce(IReadOnlyList<WorkDay> days)
    {
        var seen = new HashSet<DayOfWeek>();
        foreach (var day in days)
        {
            if (!seen.Add(day.Day)) throw new DomainException(SlaErrors.DayListedTwice, $"{day.Day} appears twice.");
        }
    }

    // Checked before anything changes, so a refused change leaves the calendar as it was.
    private void EnsureOneOpenDayAfter(IReadOnlyList<WorkDay> incoming)
    {
        var named = new HashSet<DayOfWeek>();
        foreach (var day in incoming)
        {
            if (day.IsOpen) return;
            named.Add(day.Day);
        }

        foreach (var day in _days)
        {
            if (day.IsOpen && !named.Contains(day.Day)) return;
        }
        throw new DomainException(SlaErrors.CalendarHasNoOpenDay);
    }

    private WorkDay? FindDay(DayOfWeek day)
    {
        foreach (var existing in _days)
        {
            if (existing.Day == day) return existing;
        }
        return null;
    }

    private Holiday? FindHoliday(DateOnly date)
    {
        foreach (var holiday in _holidays)
        {
            if (holiday.Date == date) return holiday;
        }
        return null;
    }

    // Every change, even a new holiday row, touches the root row, so its xmin changes and a second admin working from an older
    // copy is refused.
    private void Touch(UserId by, string byName, DateTime nowUtc)
    {
        UpdatedUtc = nowUtc;
        UpdatedById = by;
        UpdatedByName = byName;
    }
}
