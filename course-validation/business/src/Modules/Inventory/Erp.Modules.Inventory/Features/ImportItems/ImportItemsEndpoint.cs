using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Inventory.Features;

internal sealed record ImportItemsRequest(IReadOnlyList<ItemImportRow> Rows, bool Commit = false);

internal sealed class ImportItemsRequestValidator : AbstractValidator<ImportItemsRequest>
{
    public ImportItemsRequestValidator()
    {
        RuleFor(x => x.Rows).NotEmpty().Must(r => r.Count <= ImportLimits.MaxRows)
            .WithMessage($"Import at most {ImportLimits.MaxRows} rows at a time.");
        RuleForEach(x => x.Rows).ChildRules(r =>
        {
            r.RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
            r.RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
            r.RuleFor(x => x.Unit).NotEmpty().MaximumLength(10);
            r.RuleFor(x => x.Quantity).GreaterThanOrEqualTo(0).Must(q => decimal.Round(q, 3) == q)
                .WithMessage("Quantity allows at most three decimal places.");
            r.RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0);
        });
    }
}

internal static class ImportItemsEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/items/import", async (ImportItemsRequest body, ImportItemsHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(new ImportItemsCommand(body.Rows, body.Commit), ct)))
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));
}
