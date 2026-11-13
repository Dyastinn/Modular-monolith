using Erp.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Identity.Features;

internal static class SearchSignInAttemptsEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/sign-in-attempts", async ([AsParameters] SearchSignInAttemptsQuery query, SearchSignInAttemptsHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(query, ct)))
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));
}