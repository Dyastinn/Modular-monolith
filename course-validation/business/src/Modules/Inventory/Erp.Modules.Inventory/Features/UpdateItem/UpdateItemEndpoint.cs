using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Inventory.Features;


internal sealed record UpdateItemRequest(string Name, string Unit, bool AllowsFractions);

internal sealed class UpdateItemRequestValidator : AbstractValidator<UpdateItemRequest>
{
    public UpdateItemRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Unit).NotEmpty().MaximumLength(10);
    }
}

internal static class UpdateItemEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPut("/items/{id:long}",
                async Task<Results<NoContent, NotFound>> (long id, UpdateItemRequest body, UpdateItemHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new UpdateItemCommand(id, body.Name, body.Unit, body.AllowsFractions), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(Roles.InventoryClerk, Roles.Admin));
}