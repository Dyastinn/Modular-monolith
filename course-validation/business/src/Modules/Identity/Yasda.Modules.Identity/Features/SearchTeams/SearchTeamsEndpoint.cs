using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Identity.Features;

internal static class SearchTeamsEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/teams", async ([AsParameters] SearchTeamsQuery query, SearchTeamsHandler handler, CancellationToken ct) =>
        {
            var teams = await handler.HandleAsync(query, ct);
            return TypedResults.Ok(teams);
        })
        .RequireAuthorization(policy => policy.RequireRole(TeamRoles.Readers));
    }
}
