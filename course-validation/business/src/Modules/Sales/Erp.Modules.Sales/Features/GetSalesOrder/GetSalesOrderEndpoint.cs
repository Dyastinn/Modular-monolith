using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;


internal static class GetSalesOrderEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/sales-orders/{id:long}",
                async Task<Results<Ok<SalesOrderDetailDto>, NotFound>> (long id, GetSalesOrderHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new GetSalesOrderQuery(id), ct) is { } order ? TypedResults.Ok(order) : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Readers));
}