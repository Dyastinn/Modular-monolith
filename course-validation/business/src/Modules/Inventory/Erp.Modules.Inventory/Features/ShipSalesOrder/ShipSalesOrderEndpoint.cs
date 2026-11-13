using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Inventory.Features;

internal sealed record ShipSalesOrderRequest(long SalesOrderId);

internal sealed class ShipSalesOrderRequestValidator : AbstractValidator<ShipSalesOrderRequest>
{
    public ShipSalesOrderRequestValidator() => RuleFor(x => x.SalesOrderId).GreaterThan(0);
}

internal static class ShipSalesOrderEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/shipments", async Task<Created<CreatedId>> (ShipSalesOrderRequest body, ShipSalesOrderHandler handler, CancellationToken ct) =>
            {
                var id = await handler.HandleAsync(new ShipSalesOrderCommand(body.SalesOrderId), ct);
                return TypedResults.Created($"/api/shipments/{id}", new CreatedId(id));
            })
            .RequireAuthorization(p => p.RequireRole(Roles.InventoryClerk, Roles.Admin));
}