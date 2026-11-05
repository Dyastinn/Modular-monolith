using Carter;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Inventory.Features;

internal sealed record UpdateItemRequest(string Name, string Unit, bool AllowsFractions);

internal sealed class UpdateItemEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPut("/items/{id:long}", async (long id, UpdateItemRequest body, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new UpdateItemCommand(id, body.Name, body.Unit, body.AllowsFractions), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(Roles.InventoryClerk, Roles.Admin));
}
