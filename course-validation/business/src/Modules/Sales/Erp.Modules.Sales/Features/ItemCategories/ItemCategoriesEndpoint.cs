using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;


internal sealed record ItemCategoryRequest(string Name, decimal MaxQuantityPerOrder, string? RequiredRole);

internal sealed class ItemCategoryRequestValidator : AbstractValidator<ItemCategoryRequest>
{
    public ItemCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.MaxQuantityPerOrder).GreaterThan(0);
    }
}

internal static class ItemCategoriesEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/item-categories", async (ListItemCategoriesHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(ct)))
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Readers));

        app.MapPost("/item-categories", async Task<Created<CreatedId>> (ItemCategoryRequest body, SaveItemCategoryHandler handler, CancellationToken ct) =>
            {
                var id = await handler.HandleAsync(new SaveItemCategoryCommand(null, body.Name, body.MaxQuantityPerOrder, body.RequiredRole), ct);
                return TypedResults.Created($"/api/item-categories/{id}", new CreatedId(id!.Value));
            })
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Managers));

        app.MapPut("/item-categories/{id:long}",
                async Task<Results<NoContent, NotFound>> (long id, ItemCategoryRequest body, SaveItemCategoryHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new SaveItemCategoryCommand(id, body.Name, body.MaxQuantityPerOrder, body.RequiredRole), ct) is null
                        ? TypedResults.NotFound()
                        : TypedResults.NoContent())
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Managers));
    }
}