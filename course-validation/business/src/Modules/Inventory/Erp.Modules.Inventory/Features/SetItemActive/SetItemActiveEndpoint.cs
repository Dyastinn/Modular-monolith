using Carter;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Inventory.Features;

internal sealed class SetItemActiveEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/items/{id:long}/activate", async (long id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new SetItemActiveCommand(id, true), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(Roles.InventoryClerk, Roles.Admin));

        app.MapPost("/items/{id:long}/deactivate", async (long id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new SetItemActiveCommand(id, false), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(Roles.InventoryClerk, Roles.Admin));
    }
}
