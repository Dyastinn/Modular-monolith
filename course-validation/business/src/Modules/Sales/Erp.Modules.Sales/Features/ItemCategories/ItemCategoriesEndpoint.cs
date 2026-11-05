using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed record ItemCategoryRequest(string Name, decimal MaxQuantityPerOrder, string? RequiredRole);

internal sealed class ItemCategoriesEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/item-categories", async (ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new ListItemCategoriesQuery(), ct)))
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Readers));

        app.MapPost("/item-categories", async (ItemCategoryRequest body, ISender sender, CancellationToken ct) =>
            {
                var id = await sender.Send(new SaveItemCategoryCommand(null, body.Name, body.MaxQuantityPerOrder, body.RequiredRole), ct);
                return Results.Created($"/api/item-categories/{id}", new { id });
            })
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Managers));

        app.MapPut("/item-categories/{id:long}", async (long id, ItemCategoryRequest body, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new SaveItemCategoryCommand(id, body.Name, body.MaxQuantityPerOrder, body.RequiredRole), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Managers));
    }
}
