using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Identity.Features;

// Identity's own /auth/manage/info has no role; a client needs it to decide what to show.
internal static class GetCurrentUserEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/auth/me", async (GetCurrentUserHandler handler, CancellationToken ct) =>
        {
            var me = await handler.HandleAsync(ct);
            return TypedResults.Ok(me);
        })
        .RequireAuthorization();
    }
}
