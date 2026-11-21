using AwesomeAssertions;
using Yasda.Modules.Sla.Domain;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Sla.UnitTests;

public class HolidayImportTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 9, 0, 0, DateTimeKind.Utc);
    private static readonly UserId Admin = new(1);

    private static WorkCalendar NewCalendar()
    {
        return WorkCalendar.Create("Asia/Manila", [new WorkDay(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(17, 0))], Admin, "Dyst", Now);
    }

    private static ErrorCode CodeOf(Action act)
    {
        return act.Should().Throw<DomainException>().Which.Error;
    }

    [Fact]
    public void A_csv_with_a_header_blank_lines_and_quoted_names_is_read()
    {
        var csv = "Date,Name\r\n2026-12-25,Christmas Day\r\n\r\n 2026-12-30 , \"Rizal Day\" \r\n";

        var holidays = HolidayCsv.Parse(csv);

        holidays.Should().Equal(
            new PresetHoliday(new DateOnly(2026, 12, 25), "Christmas Day"),
            new PresetHoliday(new DateOnly(2026, 12, 30), "Rizal Day"));
    }

    [Fact]
    public void A_name_may_contain_a_comma()
    {
        var holidays = HolidayCsv.Parse("2026-12-08,Feast of the Immaculate Conception, Mary");

        holidays.Single().Name.Should().Be("Feast of the Immaculate Conception, Mary");
    }

    [Theory]
    [InlineData("2026-12-25", 1)]                       // no comma
    [InlineData("25/12/2026,Christmas Day", 1)]         // not a yyyy-MM-dd date
    [InlineData("2026-12-25,", 1)]                      // no name
    [InlineData("2026-12-25,Christmas Day\n2026-13-01,Nope", 2)]
    public void A_line_that_cant_be_read_refuses_the_whole_file_and_names_the_line(string csv, int badLine)
    {
        var exception = new Action(() => HolidayCsv.Parse(csv)).Should().Throw<DomainException>().Which;

        exception.Error.Should().Be(SlaErrors.HolidayLineInvalid);
        exception.Message.Should().StartWith($"Line {badLine}:");
    }

    [Fact]
    public void Loading_a_list_adds_the_new_dates_and_keeps_the_names_of_dates_already_there()
    {
        var calendar = NewCalendar();
        calendar.AddHoliday(new DateOnly(2026, 12, 25), "Pasko", Admin, "Dyst", Now);

        var added = calendar.AddHolidays(
            [new PresetHoliday(new DateOnly(2026, 12, 25), "Christmas Day"), new PresetHoliday(new DateOnly(2026, 12, 30), "Rizal Day")],
            Admin, "Dyst", Now.AddHours(1));

        added.Should().Be(1);
        calendar.Holidays.Select(h => h.Name).Should().BeEquivalentTo("Pasko", "Rizal Day");
        calendar.UpdatedUtc.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void Loading_the_same_list_twice_changes_nothing_the_second_time()
    {
        var calendar = NewCalendar();
        var list = HolidayPreset.Find("PH")!.Holidays.ToList();

        var first = calendar.AddHolidays(list, Admin, "Dyst", Now);
        var second = calendar.AddHolidays(list, Admin, "Dyst", Now.AddHours(1));

        first.Should().Be(list.Count);
        second.Should().Be(0);
        calendar.UpdatedUtc.Should().Be(Now, "nothing new was added, so nothing was touched");
    }

    [Fact]
    public void One_bad_holiday_in_a_list_adds_none_of_them()
    {
        var calendar = NewCalendar();

        CodeOf(() => calendar.AddHolidays(
            [new PresetHoliday(new DateOnly(2026, 12, 25), "Christmas Day"), new PresetHoliday(new DateOnly(2026, 12, 26), " ")],
            Admin, "Dyst", Now)).Should().Be(SlaErrors.HolidayNameRequired);

        calendar.Holidays.Should().BeEmpty();
    }

    [Fact]
    public void The_philippine_list_has_each_date_once_with_a_usable_name_and_only_the_two_years_proclaimed()
    {
        var preset = HolidayPreset.Find("ph")!;

        preset.Country.Should().Be("Philippines");
        preset.Holidays.Select(h => h.Date).Should().OnlyHaveUniqueItems();
        preset.Holidays.Should().OnlyContain(h => h.Date.Year == 2026 || h.Date.Year == 2027);
        preset.Holidays.Should().OnlyContain(h => h.Name.Length > 0 && h.Name.Length <= WorkCalendar.HolidayNameMaxLength);
        preset.Holidays.Count(h => h.Date.Year == 2026).Should().Be(18);
        preset.Holidays.Count(h => h.Date.Year == 2027).Should().Be(18);
        preset.Holidays.Should().NotContain(h => h.Date == new DateOnly(2026, 2, 25), "the EDSA day is a special working day");
        preset.Holidays.Should().NotContain(h => h.Name.Contains("Eid"), "their dates are proclaimed later");
    }

    [Fact]
    public void An_unknown_list_is_not_found()
    {
        HolidayPreset.Find("XX").Should().BeNull();
    }
}
