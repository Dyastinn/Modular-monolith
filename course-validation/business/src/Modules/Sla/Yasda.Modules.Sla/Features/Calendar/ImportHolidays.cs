using FluentValidation;
using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Sla.Domain;
using Yasda.Modules.Sla.Infrastructure;
using Yasda.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Sla.Features;

internal sealed record PresetDto(string Code, string Country, int Holidays);
internal sealed record ImportResult(int Added, int AlreadyThere);

internal sealed record LoadPresetCommand(string Code);
internal sealed record ImportCsvCommand(string Csv);

// Two ways to add many holidays: a country's ready-made list, and a CSV file. Both end in the calendar's AddHolidays.
internal sealed class ImportHolidaysHandler(SlaDbContext db, ICurrentUser currentUser, IUserDirectory users, TimeProvider time, ClockRecalculator recalculator)
{
    public static List<PresetDto> ListPresets()
    {
        var presets = new List<PresetDto>();
        foreach (var preset in HolidayPreset.All)
        {
            presets.Add(new PresetDto(preset.Code, preset.Country, preset.Holidays.Count));
        }
        return presets;
    }

    public async Task<ImportResult> LoadPresetAsync(LoadPresetCommand cmd, CancellationToken ct)
    {
        var preset = HolidayPreset.Find(cmd.Code);
        if (preset is null) throw new DomainException(SlaErrors.UnknownHolidayPreset, $"There is no holiday list '{cmd.Code}'.");

        return await AddAsync(preset.Holidays.ToList(), ct);
    }

    public async Task<ImportResult> ImportCsvAsync(ImportCsvCommand cmd, CancellationToken ct)
    {
        var holidays = HolidayCsv.Parse(cmd.Csv);
        return await AddAsync(holidays, ct);
    }

    private async Task<ImportResult> AddAsync(List<PresetHoliday> holidays, CancellationToken ct)
    {
        var calendar = await db.Calendars.Include(c => c.Days).Include(c => c.Holidays).SingleAsync(ct);

        var myName = await users.NameOfAsync(currentUser.UserId, ct) ?? currentUser.Username;
        var added = calendar.AddHolidays(holidays, new UserId(currentUser.UserId), myName, time.GetUtcNow().UtcDateTime);
        await recalculator.RecalculateAsync(calendar.ToBusinessCalendar(), ct);
        await db.SaveChangesAsync(ct);

        return new ImportResult(added, holidays.Count - added);
    }
}

internal sealed record LoadPresetRequest(string Code);
internal sealed record ImportCsvRequest(string Csv);

// A file bigger than this isn't a list of holidays.
internal sealed class ImportCsvRequestValidator : AbstractValidator<ImportCsvRequest>
{
    public ImportCsvRequestValidator()
    {
        RuleFor(x => x.Csv).NotEmpty().MaximumLength(100_000);
    }
}

internal static class ImportHolidaysEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/sla/calendar/holiday-presets", Ok<List<PresetDto>> () =>
        {
            return TypedResults.Ok(ImportHolidaysHandler.ListPresets());
        })
        .RequireAuthorization(policy => policy.RequireRole(SlaRoles.CalendarEditors));

        app.MapPost("/sla/calendar/holiday-presets", async Task<Ok<ImportResult>> (LoadPresetRequest body, ImportHolidaysHandler handler, CancellationToken ct) =>
        {
            var result = await handler.LoadPresetAsync(new LoadPresetCommand(body.Code), ct);
            return TypedResults.Ok(result);
        })
        .RequireAuthorization(policy => policy.RequireRole(SlaRoles.CalendarEditors));

        app.MapPost("/sla/calendar/holidays/import", async Task<Ok<ImportResult>> (ImportCsvRequest body, ImportHolidaysHandler handler, CancellationToken ct) =>
        {
            var result = await handler.ImportCsvAsync(new ImportCsvCommand(body.Csv), ct);
            return TypedResults.Ok(result);
        })
        .RequireAuthorization(policy => policy.RequireRole(SlaRoles.CalendarEditors));
    }
}
