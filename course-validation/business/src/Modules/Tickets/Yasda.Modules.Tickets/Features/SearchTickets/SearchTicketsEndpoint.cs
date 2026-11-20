using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Tickets.Features;

internal static class SearchTicketsEndpoint
{
    // The query string binds straight onto the query record; its defaults make every parameter optional.
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/tickets", async ([AsParameters] SearchTicketsQuery query, SearchTicketsHandler handler, CancellationToken ct) =>
        {
            var page = await handler.HandleAsync(query, ct);
            return TypedResults.Ok(page);
        })
        .RequireAuthorization();
    }
}
