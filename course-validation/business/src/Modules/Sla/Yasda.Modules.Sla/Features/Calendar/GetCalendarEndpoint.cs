using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Sla.Features;

internal static class GetCalendarEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/sla/calendar", async Task<Ok<CalendarDto>> (GetCalendarHandler handler, CancellationToken ct) =>
        {
            var calendar = await handler.HandleAsync(ct);
            return TypedResults.Ok(calendar);
        })
        .RequireAuthorization(policy => policy.RequireRole(SlaRoles.Readers));
    }
}
