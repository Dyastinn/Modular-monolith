using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Invoicing.Features;

internal sealed record RecordWithholdingCertificateRequest(long CustomerId, int Year, int Quarter, decimal Amount, string? Reference,
    DateOnly ReceivedOn);

internal sealed class WithholdingTaxEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/withholding-tax", async (int year, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new WithholdingSummaryQuery(year), ct)))
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Accountants));

        app.MapPost("/withholding-certificates",
                async (RecordWithholdingCertificateRequest body, ISender sender, CancellationToken ct) =>
                {
                    var id = await sender.Send(new RecordWithholdingCertificateCommand(body.CustomerId, body.Year, body.Quarter,
                        body.Amount, body.Reference, body.ReceivedOn), ct);
                    return Results.Created($"/api/withholding-tax?year={body.Year}", new { id });
                })
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Accountants));

        app.MapPost("/withholding-certificates/{id:long}/void", async (long id, VoidRequest body, ISender sender, CancellationToken ct) =>
                {
                    await sender.Send(new VoidWithholdingCertificateCommand(id, body.Reason), ct);
                    return Results.NoContent();
                })
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Accountants));
    }
}
