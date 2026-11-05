using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Purchasing.Features;

internal sealed class SearchPurchaseOrdersEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/purchase-orders",
                async (long? supplierId, string? status, string? search, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
                    Results.Ok(await sender.Send(new SearchPurchaseOrdersQuery(supplierId, status, search, page ?? 1, pageSize ?? 20), ct)))
            .RequireAuthorization(p => p.RequireRole(PurchasingRoles.Readers));

        app.MapGet("/purchase-orders/{id:long}", async (long id, ISender sender, CancellationToken ct) =>
                await sender.Send(new GetPurchaseOrderQuery(id), ct) is { } po ? Results.Ok(po) : Results.NotFound())
            .RequireAuthorization(p => p.RequireRole(PurchasingRoles.Readers));
    }
}
