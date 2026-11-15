using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Inventory.Features;

// UnitCost: for stock found, what each unit is worth (left out: the item's average). Stock lost always leaves at the average.
internal sealed record AdjustStockRequest(decimal Quantity, string Reason, decimal? UnitCost = null);

internal sealed class AdjustStockRequestValidator : AbstractValidator<AdjustStockRequest>
{
    public AdjustStockRequestValidator()
    {
        RuleFor(x => x.Quantity).NotEqual(0).Must(q => decimal.Round(q, 3) == q)
            .WithMessage("Quantity must be non-zero, with at most three decimal places.");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0).When(x => x.UnitCost is not null);
    }
}

internal static class AdjustStockEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/items/{id:long}/adjustments",
                async Task<Results<NoContent, NotFound>> (long id, AdjustStockRequest body, AdjustStockHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new AdjustStockCommand(id, body.Quantity, body.Reason, body.UnitCost), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(Roles.InventoryClerk, Roles.Admin));
}