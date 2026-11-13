using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Inventory.Features;

internal static class SearchShipmentsEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/shipments", async ([AsParameters] SearchShipmentsQuery query, SearchShipmentsHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(query, ct)))
            .RequireAuthorization();
}