using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Purchasing.Features;

internal sealed record PurchaseOrderRequest(long SupplierId, DateOnly? ExpectedDate, string? Notes, IReadOnlyList<PurchaseLineInput> Lines);

internal sealed class SavePurchaseOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/purchase-orders", async (PurchaseOrderRequest body, ISender sender, CancellationToken ct) =>
            {
                var id = await sender.Send(new SavePurchaseOrderCommand(null, body.SupplierId, body.ExpectedDate, body.Notes, body.Lines), ct);
                return Results.Created($"/api/purchase-orders/{id}", new { id });
            })
            .RequireAuthorization(p => p.RequireRole(PurchasingRoles.Buyers));

        app.MapPut("/purchase-orders/{id:long}", async (long id, PurchaseOrderRequest body, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new SavePurchaseOrderCommand(id, body.SupplierId, body.ExpectedDate, body.Notes, body.Lines), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(PurchasingRoles.Buyers));
    }
}
