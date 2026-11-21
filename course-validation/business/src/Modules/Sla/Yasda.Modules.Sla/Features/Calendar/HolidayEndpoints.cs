using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Sla.Features;

internal sealed record AddHolidayRequest(DateOnly Date, string Name);

// A date outside these years is a typo, not a holiday.
internal sealed class AddHolidayRequestValidator : AbstractValidator<AddHolidayRequest>
{
    public AddHolidayRequestValidator()
    {
        RuleFor(x => x.Date).Must(date => date.Year is >= 2000 and <= 2100).WithMessage("Use a date between the years 2000 and 2100.");
    }
}

internal static class HolidayEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/sla/calendar/holidays", async Task<NoContent> (AddHolidayRequest body, AddHolidayHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new AddHolidayCommand(body.Date, body.Name), ct);
            return TypedResults.NoContent();
        })
        .RequireAuthorization(policy => policy.RequireRole(SlaRoles.CalendarEditors));

        // The date is in the address, like 2026-12-25. Removing a date that isn't a holiday is not an error.
        app.MapDelete("/sla/calendar/holidays/{date}", async Task<NoContent> (DateOnly date, RemoveHolidayHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(new RemoveHolidayCommand(date), ct);
            return TypedResults.NoContent();
        })
        .RequireAuthorization(policy => policy.RequireRole(SlaRoles.CalendarEditors));
    }
}
