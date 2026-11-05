using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Purchasing.Features;

internal sealed class SaveSupplierEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/suppliers", async (SupplierInput body, ISender sender, CancellationToken ct) =>
            {
                var id = await sender.Send(new SaveSupplierCommand(null, body), ct);
                return Results.Created($"/api/suppliers/{id}", new { id });
            })
            .RequireAuthorization(p => p.RequireRole(PurchasingRoles.Buyers));

        app.MapPut("/suppliers/{id:long}", async (long id, SupplierInput body, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new SaveSupplierCommand(id, body), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(PurchasingRoles.Buyers));

        app.MapPost("/suppliers/{id:long}/activate", async (long id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new SetSupplierActiveCommand(id, true), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(PurchasingRoles.Buyers));

        app.MapPost("/suppliers/{id:long}/deactivate", async (long id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new SetSupplierActiveCommand(id, false), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(PurchasingRoles.Buyers));
    }
}
