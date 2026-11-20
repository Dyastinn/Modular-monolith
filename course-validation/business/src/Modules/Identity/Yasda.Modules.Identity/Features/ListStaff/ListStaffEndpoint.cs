using Yasda.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Identity.Features;

internal static class ListStaffEndpoint
{
    // Who a ticket can be assigned to; ?teamId= narrows it to one team. Staff only: requesters have no reason to list the team.
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/staff", async ([AsParameters] ListStaffQuery query, ListStaffHandler handler, CancellationToken ct) =>
        {
            var staff = await handler.HandleAsync(query, ct);
            return TypedResults.Ok(staff);
        })
        .RequireAuthorization(p => p.RequireRole(Roles.Agent, Roles.TeamLead, Roles.Admin));
    }
}
