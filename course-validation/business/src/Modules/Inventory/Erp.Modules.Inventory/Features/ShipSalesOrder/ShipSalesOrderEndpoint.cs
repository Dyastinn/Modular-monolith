using Carter;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Inventory.Features;

internal sealed record ShipSalesOrderRequest(long SalesOrderId);

internal sealed class ShipSalesOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/shipments", async (ShipSalesOrderRequest body, ISender sender, CancellationToken ct) =>
            {
                var id = await sender.Send(new ShipSalesOrderCommand(body.SalesOrderId), ct);
                return Results.Created($"/api/shipments/{id}", new { id });
            })
            .RequireAuthorization(p => p.RequireRole(Roles.InventoryClerk, Roles.Admin));
}
