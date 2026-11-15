using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Invoicing.Features;

internal sealed record ImportOpeningBalancesRequest(IReadOnlyList<OpeningBalanceRow> Rows, bool Commit = false);

internal sealed class ImportOpeningBalancesRequestValidator : AbstractValidator<ImportOpeningBalancesRequest>
{
    public ImportOpeningBalancesRequestValidator()
    {
        RuleFor(x => x.Rows).NotEmpty().Must(r => r.Count <= ImportLimits.MaxRows)
            .WithMessage($"Import at most {ImportLimits.MaxRows} rows at a time.");
        RuleForEach(x => x.Rows).ChildRules(r =>
        {
            r.RuleFor(x => x.Customer).NotEmpty().MaximumLength(200);
            r.RuleFor(x => x.Reference).NotEmpty().MaximumLength(30);
            r.RuleFor(x => x.Amount).GreaterThan(0);
        });
    }
}

internal static class ImportOpeningBalancesEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/opening-balances/import", async (ImportOpeningBalancesRequest body, ImportOpeningBalancesHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(new ImportOpeningBalancesCommand(body.Rows, body.Commit), ct)))
            .RequireAuthorization(p => p.RequireRole(Roles.Accountant, Roles.Admin));
}
