using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed record ImportPricesRequest(IReadOnlyList<PriceRow> Rows, bool Commit = false);

internal sealed class ImportPricesRequestValidator : AbstractValidator<ImportPricesRequest>
{
    public ImportPricesRequestValidator()
    {
        RuleFor(x => x.Rows).NotEmpty().Must(r => r.Count <= ImportLimits.MaxRows)
            .WithMessage($"Import at most {ImportLimits.MaxRows} rows at a time.");
        RuleForEach(x => x.Rows).ChildRules(r =>
        {
            r.RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
            r.RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
            r.RuleFor(x => x.Category).NotEmpty().MaximumLength(100);
            r.RuleFor(x => x.TaxType).IsInEnum();
        });
    }
}

internal static class ImportPricesEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/price-list/import", async (ImportPricesRequest body, ImportPricesHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(new ImportPricesCommand(body.Rows, body.Commit), ct)))
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));
}
