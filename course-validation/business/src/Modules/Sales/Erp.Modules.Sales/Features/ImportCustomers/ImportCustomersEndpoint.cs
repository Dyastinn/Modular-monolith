using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed record ImportCustomersRequest(IReadOnlyList<CustomerInput> Rows, bool Commit = false);

internal sealed class ImportCustomersRequestValidator : AbstractValidator<ImportCustomersRequest>
{
    public ImportCustomersRequestValidator()
    {
        RuleFor(x => x.Rows).NotEmpty().Must(r => r.Count <= ImportLimits.MaxRows)
            .WithMessage($"Import at most {ImportLimits.MaxRows} rows at a time.");
        RuleForEach(x => x.Rows).SetValidator(new CustomerInputValidator());
    }
}

internal static class ImportCustomersEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/customers/import", async (ImportCustomersRequest body, ImportCustomersHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(new ImportCustomersCommand(body.Rows, body.Commit), ct)))
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));
}
