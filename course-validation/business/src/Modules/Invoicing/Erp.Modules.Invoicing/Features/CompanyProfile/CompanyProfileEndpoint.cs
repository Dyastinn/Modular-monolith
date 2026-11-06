using Carter;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Invoicing.Features;

internal sealed record UpdateCompanyProfileRequest(string RegisteredName, string? TradeName, string Tin, string RegisteredAddress,
    TaxRegime TaxRegime, decimal VatRate, string InvoiceTitle, string? PermitNumber, string? SeriesRange, string? PermitDateIssued);

internal sealed class CompanyProfileEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/company-profile", async (ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetCompanyProfileQuery(), ct)))
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Accountants));

        app.MapPut("/company-profile", async (UpdateCompanyProfileRequest b, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new UpdateCompanyProfileCommand(b.RegisteredName, b.TradeName, b.Tin, b.RegisteredAddress,
                    b.TaxRegime, b.VatRate, b.InvoiceTitle, b.PermitNumber, b.SeriesRange, b.PermitDateIssued), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Accountants));
    }
}
