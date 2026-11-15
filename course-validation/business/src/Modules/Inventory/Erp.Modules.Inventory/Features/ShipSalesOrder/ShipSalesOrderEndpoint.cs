using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Inventory.Features;

// Lines: what goes out now. Left out: everything still to ship.
internal sealed record ShipSalesOrderRequest(long SalesOrderId, IReadOnlyList<ShipLineRequest>? Lines = null);
internal sealed record ShipLineRequest(long ItemId, decimal Quantity);

internal sealed class ShipSalesOrderRequestValidator : AbstractValidator<ShipSalesOrderRequest>
{
    public ShipSalesOrderRequestValidator()
    {
        RuleFor(x => x.SalesOrderId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().When(x => x.Lines is not null).WithMessage("List at least one line, or leave Lines out to ship everything.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.ItemId).GreaterThan(0);
            l.RuleFor(x => x.Quantity).ValidQuantity();
        });
    }
}

internal static class ShipSalesOrderEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/shipments", async Task<Created<CreatedId>> (ShipSalesOrderRequest body, ShipSalesOrderHandler handler, CancellationToken ct) =>
            {
                var id = await handler.HandleAsync(new ShipSalesOrderCommand(body.SalesOrderId,
                    body.Lines?.Select(l => (l.ItemId, l.Quantity)).ToList()), ct);
                return TypedResults.Created($"/api/shipments/{id}", new CreatedId(id));
            })
            .RequireAuthorization(p => p.RequireRole(Roles.InventoryClerk, Roles.Admin));
}