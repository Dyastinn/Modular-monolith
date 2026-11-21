using AwesomeAssertions;
using Yasda.Modules.Sla.Domain;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Sla.UnitTests;

public class WorkCalendarTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 9, 0, 0, DateTimeKind.Utc);
    private static readonly UserId Admin = new(1);

    private static TimeOnly At(int hour) => new(hour, 0);

    private static List<WorkDay> OfficeWeek()
    {
        var days = new List<WorkDay>();
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
        {
            days.Add(new WorkDay(day, At(8), At(17)));
        }
        days.Add(new WorkDay(DayOfWeek.Saturday, null, null));
        days.Add(new WorkDay(DayOfWeek.Sunday, null, null));
        return days;
    }

    private static WorkCalendar NewCalendar()
    {
        return WorkCalendar.Create("Asia/Manila", OfficeWeek(), Admin, "Dyst", Now);
    }

    private static ErrorCode CodeOf(Action act)
    {
        return act.Should().Throw<DomainException>().Which.Error;
    }

    [Fact]
    public void A_new_calendar_has_its_zone_its_week_and_no_holidays()
    {
        var calendar = NewCalendar();

        calendar.TimeZoneId.Should().Be("Asia/Manila");
        calendar.Days.Should().HaveCount(7);
        calendar.Holidays.Should().BeEmpty();
        calendar.UpdatedByName.Should().Be("Dyst");
    }

    [Fact]
    public void Changing_a_day_replaces_its_hours_and_leaves_the_other_days_alone()
    {
        var calendar = NewCalendar();

        calendar.Change("Asia/Manila", [new WorkDay(DayOfWeek.Friday, At(8), At(12))], Admin, "Dyst", Now.AddHours(1));

        calendar.Days.Single(d => d.Day == DayOfWeek.Friday).Closes.Should().Be(At(12));
        calendar.Days.Single(d => d.Day == DayOfWeek.Monday).Closes.Should().Be(At(17));
        calendar.Days.Should().HaveCount(7);
        calendar.UpdatedUtc.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void A_day_can_be_closed_by_sending_no_times()
    {
        var calendar = NewCalendar();

        calendar.Change("Asia/Manila", [new WorkDay(DayOfWeek.Friday, null, null)], Admin, "Dyst", Now);

        calendar.Days.Single(d => d.Day == DayOfWeek.Friday).IsOpen.Should().BeFalse();
    }

    [Fact]
    public void An_unknown_time_zone_is_refused_and_changes_nothing()
    {
        var calendar = NewCalendar();

        CodeOf(() => calendar.Change("Mars/Olympus", [new WorkDay(DayOfWeek.Monday, At(9), At(18))], Admin, "Dyst", Now))
            .Should().Be(SlaErrors.UnknownTimeZone);

        calendar.TimeZoneId.Should().Be("Asia/Manila");
        calendar.Days.Single(d => d.Day == DayOfWeek.Monday).Opens.Should().Be(At(8));
    }

    [Fact]
    public void Closing_every_open_day_is_refused_and_changes_nothing()
    {
        var calendar = NewCalendar();
        var closeAll = new List<WorkDay>();
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            closeAll.Add(new WorkDay(day, null, null));
        }

        CodeOf(() => calendar.Change("Asia/Manila", closeAll, Admin, "Dyst", Now)).Should().Be(SlaErrors.CalendarHasNoOpenDay);

        calendar.Days.Single(d => d.Day == DayOfWeek.Monday).IsOpen.Should().BeTrue();
    }

    [Fact]
    public void Closing_the_last_open_day_while_others_stay_closed_is_refused()
    {
        var calendar = WorkCalendar.Create("Asia/Manila", [new WorkDay(DayOfWeek.Monday, At(8), At(17))], Admin, "Dyst", Now);

        CodeOf(() => calendar.Change("Asia/Manila", [new WorkDay(DayOfWeek.Monday, null, null)], Admin, "Dyst", Now))
            .Should().Be(SlaErrors.CalendarHasNoOpenDay);
    }

    [Fact]
    public void A_weekday_listed_twice_is_refused()
    {
        var calendar = NewCalendar();

        CodeOf(() => calendar.Change("Asia/Manila", [new WorkDay(DayOfWeek.Monday, At(8), At(9)), new WorkDay(DayOfWeek.Monday, At(9), At(10))], Admin, "Dyst", Now))
            .Should().Be(SlaErrors.DayListedTwice);
    }

    [Fact]
    public void A_day_needs_both_times_or_neither_and_must_close_after_it_opens()
    {
        CodeOf(() => { _ = new WorkDay(DayOfWeek.Monday, At(8), null); }).Should().Be(SlaErrors.ClosingBeforeOpening);
        CodeOf(() => { _ = new WorkDay(DayOfWeek.Monday, null, At(17)); }).Should().Be(SlaErrors.ClosingBeforeOpening);
        CodeOf(() => { _ = new WorkDay(DayOfWeek.Monday, At(17), At(8)); }).Should().Be(SlaErrors.ClosingBeforeOpening);
        CodeOf(() => { _ = new WorkDay(DayOfWeek.Monday, At(8), At(8)); }).Should().Be(SlaErrors.ClosingBeforeOpening);
    }

    [Fact]
    public void A_holiday_is_added_with_a_trimmed_name_and_touches_the_calendar()
    {
        var calendar = NewCalendar();

        calendar.AddHoliday(new DateOnly(2026, 12, 25), "  Christmas Day ", Admin, "Dyst", Now.AddHours(2));

        calendar.Holidays.Should().ContainSingle().Which.Name.Should().Be("Christmas Day");
        calendar.UpdatedUtc.Should().Be(Now.AddHours(2));
    }

    [Fact]
    public void A_holiday_needs_a_name_and_a_date_can_be_a_holiday_only_once()
    {
        var calendar = NewCalendar();
        var date = new DateOnly(2026, 12, 25);

        CodeOf(() => calendar.AddHoliday(date, "  ", Admin, "Dyst", Now)).Should().Be(SlaErrors.HolidayNameRequired);
        CodeOf(() => calendar.AddHoliday(date, new string('x', WorkCalendar.HolidayNameMaxLength + 1), Admin, "Dyst", Now)).Should().Be(SlaErrors.HolidayNameRequired);

        calendar.AddHoliday(date, "Christmas Day", Admin, "Dyst", Now);
        CodeOf(() => calendar.AddHoliday(date, "Again", Admin, "Dyst", Now)).Should().Be(SlaErrors.HolidayAlreadyListed);
    }

    [Fact]
    public void Removing_a_holiday_removes_it_and_removing_a_date_that_is_not_one_changes_nothing()
    {
        var calendar = NewCalendar();
        calendar.AddHoliday(new DateOnly(2026, 12, 25), "Christmas Day", Admin, "Dyst", Now);

        calendar.RemoveHoliday(new DateOnly(2026, 12, 24), Admin, "Dyst", Now.AddHours(1));
        calendar.Holidays.Should().HaveCount(1);
        calendar.UpdatedUtc.Should().Be(Now);

        calendar.RemoveHoliday(new DateOnly(2026, 12, 25), Admin, "Dyst", Now.AddHours(2));
        calendar.Holidays.Should().BeEmpty();
        calendar.UpdatedUtc.Should().Be(Now.AddHours(2));
    }

    [Fact]
    public void The_calendar_turns_into_the_pure_business_calendar_with_its_holidays()
    {
        var calendar = NewCalendar();
        calendar.AddHoliday(new DateOnly(2026, 11, 30), "Bonifacio Day", Admin, "Dyst", Now);
        var manila = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");
        var friday4pm = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, 27, 16, 0, 0), manila);

        var due = calendar.ToBusinessCalendar().Add(friday4pm, TimeSpan.FromHours(2));

        // One hour on Friday, Monday the 30th is a holiday, so the second hour is Tuesday 08:00-09:00.
        due.Should().Be(TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 12, 1, 9, 0, 0), manila));
    }
}
