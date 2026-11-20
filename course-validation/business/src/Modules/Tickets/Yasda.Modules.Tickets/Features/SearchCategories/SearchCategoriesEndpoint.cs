using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Tickets.Features;

internal static class SearchCategoriesEndpoint
{
    // Anyone signed in reads the categories in use: the "New ticket" form needs them.
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/categories", async ([AsParameters] SearchCategoriesQuery query, SearchCategoriesHandler handler, CancellationToken ct) =>
        {
            var categories = await handler.HandleAsync(query, ct);
            return TypedResults.Ok(categories);
        })
        .RequireAuthorization();
    }
}
