using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Identity.Features;

internal static class GetTeamEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/teams/{id:long}", async Task<Results<Ok<TeamDetailDto>, NotFound>> (long id, GetTeamHandler handler, CancellationToken ct) =>
        {
            var team = await handler.HandleAsync(id, ct);
            if (team is null) return TypedResults.NotFound();
            return TypedResults.Ok(team);
        })
        .RequireAuthorization(policy => policy.RequireRole(TeamRoles.Readers));
    }
}
