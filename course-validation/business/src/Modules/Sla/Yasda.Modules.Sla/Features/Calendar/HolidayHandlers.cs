using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Sla.Domain;
using Yasda.Modules.Sla.Infrastructure;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Sla.Features;

internal sealed record AddHolidayCommand(DateOnly Date, string Name);
internal sealed record RemoveHolidayCommand(DateOnly Date);

internal sealed class AddHolidayHandler(SlaDbContext db, ICurrentUser currentUser, IUserDirectory users, TimeProvider time, ClockRecalculator recalculator)
{
    public async Task HandleAsync(AddHolidayCommand cmd, CancellationToken ct)
    {
        var calendar = await db.Calendars.Include(c => c.Days).Include(c => c.Holidays).SingleAsync(ct);

        var myName = await users.NameOfAsync(currentUser.UserId, ct) ?? currentUser.Username;
        calendar.AddHoliday(cmd.Date, cmd.Name, new UserId(currentUser.UserId), myName, time.GetUtcNow().UtcDateTime);
        await recalculator.RecalculateAsync(calendar.ToBusinessCalendar(), ct);
        await db.SaveChangesAsync(ct);
    }
}

internal sealed class RemoveHolidayHandler(SlaDbContext db, ICurrentUser currentUser, IUserDirectory users, TimeProvider time, ClockRecalculator recalculator)
{
    public async Task HandleAsync(RemoveHolidayCommand cmd, CancellationToken ct)
    {
        var calendar = await db.Calendars.Include(c => c.Days).Include(c => c.Holidays).SingleAsync(ct);

        var myName = await users.NameOfAsync(currentUser.UserId, ct) ?? currentUser.Username;
        calendar.RemoveHoliday(cmd.Date, new UserId(currentUser.UserId), myName, time.GetUtcNow().UtcDateTime);
        await recalculator.RecalculateAsync(calendar.ToBusinessCalendar(), ct);
        await db.SaveChangesAsync(ct);
    }
}
