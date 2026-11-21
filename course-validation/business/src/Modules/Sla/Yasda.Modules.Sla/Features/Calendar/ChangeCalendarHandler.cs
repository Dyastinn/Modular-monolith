using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Sla.Domain;
using Yasda.Modules.Sla.Infrastructure;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Sla.Features;

internal sealed record DayHours(DayOfWeek Day, TimeOnly? Opens, TimeOnly? Closes);
internal sealed record ChangeCalendarCommand(string TimeZoneId, List<DayHours> Days);

internal sealed class ChangeCalendarHandler(SlaDbContext db, ICurrentUser currentUser, IUserDirectory users, TimeProvider time, CompanyZone companyZone)
{
    public async Task HandleAsync(ChangeCalendarCommand cmd, CancellationToken ct)
    {
        var calendar = await db.Calendars.Include(c => c.Days).SingleAsync(ct);

        var days = new List<WorkDay>();
        foreach (var hours in cmd.Days)
        {
            days.Add(new WorkDay(hours.Day, hours.Opens, hours.Closes));
        }

        var myName = await users.NameOfAsync(currentUser.UserId, ct) ?? currentUser.Username;
        calendar.Change(cmd.TimeZoneId, days, new UserId(currentUser.UserId), myName, time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);

        // Saved, so from now on "today" means today in the new zone.
        companyZone.Change(TimeZoneInfo.FindSystemTimeZoneById(calendar.TimeZoneId));
    }
}
