using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Inventory.Features;

internal sealed record CreateItemRequest(string Sku, string Name, string Unit, bool AllowsFractions);

internal sealed class CreateItemRequestValidator : AbstractValidator<CreateItemRequest>
{
    public CreateItemRequestValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Unit).NotEmpty().MaximumLength(10);
    }
}

internal static class CreateItemEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/items", async Task<Created<CreatedId>> (CreateItemRequest body, CreateItemHandler handler, CancellationToken ct) =>
            {
                var id = await handler.HandleAsync(new CreateItemCommand(body.Sku, body.Name, body.Unit, body.AllowsFractions), ct);
                return TypedResults.Created($"/api/items/{id}", new CreatedId(id));
            })
            .RequireAuthorization(p => p.RequireRole(Roles.InventoryClerk, Roles.Admin));
}