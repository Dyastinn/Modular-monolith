using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Knowledge.Features;

internal static class PopularArticlesEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/kb/popular", async (PopularArticlesHandler handler, CancellationToken ct) =>
        {
            var articles = await handler.HandleAsync(new PopularArticlesQuery(), ct);
            return TypedResults.Ok(articles);
        })
        .RequireAuthorization();
    }
}
