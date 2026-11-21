using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Knowledge.Features;

internal static class GetPublishedArticleEndpoint
{
    // An article the reader may not see is a 404, not a 403: nothing may hint that it exists.
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/kb/articles/{id:long}", async Task<Results<Ok<PublishedArticleDto>, NotFound>> (long id, GetPublishedArticleHandler handler, CancellationToken ct) =>
        {
            var article = await handler.HandleAsync(new GetPublishedArticleQuery(id), ct);
            if (article is null) return TypedResults.NotFound();
            return TypedResults.Ok(article);
        })
        .RequireAuthorization();
    }
}
