using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Knowledge.Features;

internal static class BrowsePublishedArticlesEndpoint
{
    // Anyone signed in; the handler decides which visibility they get.
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/kb/articles", async ([AsParameters] BrowsePublishedArticlesQuery query, BrowsePublishedArticlesHandler handler, CancellationToken ct) =>
        {
            var page = await handler.HandleAsync(query, ct);
            return TypedResults.Ok(page);
        })
        .RequireAuthorization();
    }
}
