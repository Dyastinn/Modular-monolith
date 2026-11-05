using Carter;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Inventory.Features;

internal sealed record AdjustStockRequest(decimal Quantity, string Reason);

internal sealed class AdjustStockEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/items/{id:long}/adjustments", async (long id, AdjustStockRequest body, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new AdjustStockCommand(id, body.Quantity, body.Reason), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(Roles.InventoryClerk, Roles.Admin));
}
