using Dapper;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Sla.Features;

internal sealed record WorkDayDto(string DayOfWeek, TimeOnly? Opens, TimeOnly? Closes);
internal sealed record HolidayDto(DateOnly HolidayDate, string Name);
internal sealed record CalendarHeader(string TimeZoneId, DateTime UpdatedUtc, long UpdatedById, string UpdatedByName);
internal sealed record CalendarDto(string TimeZoneId, DateTime UpdatedUtc, long UpdatedById, string UpdatedByName,
    List<WorkDayDto> Days, List<HolidayDto> Holidays);

internal sealed class GetCalendarHandler(IDbConnectionFactory connections)
{
    public async Task<CalendarDto> HandleAsync(CancellationToken ct)
    {
        await using var conn = connections.Create();
        var header = await conn.QuerySingleAsync<CalendarHeader>(new CommandDefinition("SELECT * FROM sla.get_calendar()", cancellationToken: ct));
        var days = await conn.QueryAsync<WorkDayDto>(new CommandDefinition("SELECT * FROM sla.get_calendar_days()", cancellationToken: ct));
        var holidays = await conn.QueryAsync<HolidayDto>(new CommandDefinition("SELECT * FROM sla.get_calendar_holidays()", cancellationToken: ct));

        return new CalendarDto(header.TimeZoneId, header.UpdatedUtc, header.UpdatedById, header.UpdatedByName, days.ToList(), holidays.ToList());
    }
}
