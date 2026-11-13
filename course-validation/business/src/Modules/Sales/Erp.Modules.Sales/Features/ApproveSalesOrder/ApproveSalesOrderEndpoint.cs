using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal static class ApproveSalesOrderEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/sales-orders/{id:long}/approve",
                async Task<Results<NoContent, NotFound>> (long id, ApproveSalesOrderHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new ApproveSalesOrderCommand(id), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Managers));
}