using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal static class SearchPriceListEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/price-list", async ([AsParameters] SearchPriceListQuery query, SearchPriceListHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(query, ct)))
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Readers));
}