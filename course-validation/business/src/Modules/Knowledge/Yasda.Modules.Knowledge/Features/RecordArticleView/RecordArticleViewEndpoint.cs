using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Knowledge.Features;

internal static class RecordArticleViewEndpoint
{
    // A POST, because counting a view changes something: a link prefetch or a crawler's GET must not count. The article page
    // calls it once it has shown the article.
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/kb/articles/{id:long}/view", async Task<Results<NoContent, NotFound>> (long id, RecordArticleViewHandler handler, CancellationToken ct) =>
        {
            var counted = await handler.HandleAsync(new RecordArticleViewCommand(id), ct);
            if (!counted) return TypedResults.NotFound();
            return TypedResults.NoContent();
        })
        .RequireAuthorization();
    }
}
