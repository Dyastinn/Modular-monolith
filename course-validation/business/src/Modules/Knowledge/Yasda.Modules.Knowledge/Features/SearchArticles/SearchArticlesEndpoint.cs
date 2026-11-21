using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Knowledge.Features;

internal static class SearchArticlesEndpoint
{
    // Staff only: this list shows drafts and articles in review. Readers have their own list of published articles.
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/articles", async ([AsParameters] SearchArticlesQuery query, SearchArticlesHandler handler, CancellationToken ct) =>
        {
            var page = await handler.HandleAsync(query, ct);
            return TypedResults.Ok(page);
        })
        .RequireAuthorization(policy => policy.RequireRole(KnowledgeRoles.Writers));
    }
}
