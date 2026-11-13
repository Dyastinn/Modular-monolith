using Erp.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Inventory.Features;

internal static class SetItemActiveEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/items/{id:long}/activate",
                async Task<Results<NoContent, NotFound>> (long id, SetItemActiveHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new SetItemActiveCommand(id, true), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(Roles.InventoryClerk, Roles.Admin));

        app.MapPost("/items/{id:long}/deactivate",
                async Task<Results<NoContent, NotFound>> (long id, SetItemActiveHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new SetItemActiveCommand(id, false), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(Roles.InventoryClerk, Roles.Admin));
    }
}