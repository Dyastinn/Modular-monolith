using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Knowledge.Features;

internal static class GetArticleEndpoint
{
    // Staff read every article's working copy, history and edit list; a requester has no business here (403).
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/articles/{id:long}", async Task<Results<Ok<ArticleDetailDto>, NotFound>> (long id, GetArticleHandler handler, CancellationToken ct) =>
        {
            var article = await handler.HandleAsync(new GetArticleQuery(id), ct);
            if (article is null) return TypedResults.NotFound();
            return TypedResults.Ok(article);
        })
        .RequireAuthorization(policy => policy.RequireRole(KnowledgeRoles.Writers));
    }
}
