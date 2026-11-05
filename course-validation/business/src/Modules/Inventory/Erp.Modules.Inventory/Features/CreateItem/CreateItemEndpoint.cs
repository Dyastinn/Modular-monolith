using Carter;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Inventory.Features;

internal sealed record CreateItemRequest(string Sku, string Name, string Unit, bool AllowsFractions);

internal sealed class CreateItemEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/items", async (CreateItemRequest body, ISender sender, CancellationToken ct) =>
            {
                var id = await sender.Send(new CreateItemCommand(body.Sku, body.Name, body.Unit, body.AllowsFractions), ct);
                return Results.Created($"/api/items/{id}", new { id });
            })
            .RequireAuthorization(p => p.RequireRole(Roles.InventoryClerk, Roles.Admin));
}
