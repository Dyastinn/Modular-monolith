using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Purchasing.Features;

internal sealed record ReceivePurchaseOrderRequest(IReadOnlyList<ReceivedLineInput> Lines);

internal sealed class ReceivePurchaseOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/purchase-orders/{id:long}/receipts", async (long id, ReceivePurchaseOrderRequest body, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new ReceivePurchaseOrderCommand(id, body.Lines), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(PurchasingRoles.Receivers));
}
