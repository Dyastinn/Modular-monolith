using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal static class CancelSalesOrderEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/sales-orders/{id:long}/cancel",
                async Task<Results<NoContent, NotFound>> (long id, CancelSalesOrderHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new CancelSalesOrderCommand(id), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Sellers));
}