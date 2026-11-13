using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;


internal static class SearchSalesOrdersEndpoint
{
    // The query string binds straight onto the query record; its defaults make every parameter optional.
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/sales-orders", async ([AsParameters] SearchSalesOrdersQuery query, SearchSalesOrdersHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(query, ct)))
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Readers));
}