using Yasda.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Identity.Features;

internal static class GetUserEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/users/{id:long}", async Task<Results<Ok<UserDto>, NotFound>> (long id, GetUserHandler handler, CancellationToken ct) =>
                await handler.HandleAsync(id, ct) is { } user ? TypedResults.Ok(user) : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));
    }
}
