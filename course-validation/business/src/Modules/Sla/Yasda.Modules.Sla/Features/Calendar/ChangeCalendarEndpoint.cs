using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Sla.Features;

internal sealed record DayHoursRequest(DayOfWeek Day, TimeOnly? Opens, TimeOnly? Closes);
internal sealed record ChangeCalendarRequest(string TimeZoneId, List<DayHoursRequest> Days);

// Only the shape is checked here. Whether the zone exists and the hours make sense is the calendar's rule (SLA-0003 to 0006).
internal sealed class ChangeCalendarRequestValidator : AbstractValidator<ChangeCalendarRequest>
{
    public ChangeCalendarRequestValidator()
    {
        RuleFor(x => x.TimeZoneId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Days).NotNull().Must(days => days is not null && days.Count <= 7).WithMessage("Send at most seven days.");
    }
}

internal static class ChangeCalendarEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPut("/sla/calendar", async Task<NoContent> (ChangeCalendarRequest body, ChangeCalendarHandler handler, CancellationToken ct) =>
        {
            var days = new List<DayHours>();
            foreach (var day in body.Days)
            {
                days.Add(new DayHours(day.Day, day.Opens, day.Closes));
            }

            await handler.HandleAsync(new ChangeCalendarCommand(body.TimeZoneId, days), ct);
            return TypedResults.NoContent();
        })
        .RequireAuthorization(policy => policy.RequireRole(SlaRoles.CalendarEditors));
    }
}
