using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Purchasing.Features;


internal static class SearchPurchaseOrdersEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/purchase-orders",
                async ([AsParameters] SearchPurchaseOrdersQuery query, SearchPurchaseOrdersHandler handler, CancellationToken ct) =>
                    TypedResults.Ok(await handler.HandleAsync(query, ct)))
            .RequireAuthorization(p => p.RequireRole(PurchasingRoles.Readers));

        app.MapGet("/purchase-orders/{id:long}",
                async Task<Results<Ok<PurchaseOrderDetailDto>, NotFound>> (long id, GetPurchaseOrderHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new GetPurchaseOrderQuery(id), ct) is { } po ? TypedResults.Ok(po) : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(PurchasingRoles.Readers));
    }
}