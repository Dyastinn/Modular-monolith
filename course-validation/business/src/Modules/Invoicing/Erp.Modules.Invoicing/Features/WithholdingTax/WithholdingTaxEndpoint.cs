using Erp.Modules.Invoicing.Domain;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Invoicing.Features;


internal sealed record RecordWithholdingCertificateRequest(long CustomerId, int Year, int Quarter, decimal Amount, string? Reference,
    DateOnly ReceivedOn);

internal sealed class RecordWithholdingCertificateRequestValidator : AbstractValidator<RecordWithholdingCertificateRequest>
{
    public RecordWithholdingCertificateRequestValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.Amount).GreaterThan(0).Must(a => decimal.Round(a, 2) == a).WithMessage("Amounts are in pesos and centavos.");
        RuleFor(x => x.Reference).MaximumLength(100);
    }
}

internal static class WithholdingTaxEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/withholding-tax", async ([AsParameters] WithholdingSummaryQuery query, WithholdingSummaryHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(query, ct)))
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Accountants));

        app.MapPost("/withholding-certificates",
                async Task<Created<CreatedId>> (RecordWithholdingCertificateRequest body, RecordWithholdingCertificateHandler handler, CancellationToken ct) =>
                {
                    var id = await handler.HandleAsync(new RecordWithholdingCertificateCommand(body.CustomerId, body.Year, body.Quarter,
                        body.Amount, body.Reference, body.ReceivedOn), ct);
                    return TypedResults.Created($"/api/withholding-tax?year={body.Year}", new CreatedId(id));
                })
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Accountants));

        app.MapPost("/withholding-certificates/{id:long}/void",
                async Task<Results<NoContent, NotFound>> (long id, VoidRequest body, VoidWithholdingCertificateHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new VoidWithholdingCertificateCommand(id, body.Reason), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Accountants));
    }
}