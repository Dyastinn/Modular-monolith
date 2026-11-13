using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Invoicing.Features;

internal sealed record UpdateCompanyProfileRequest(string RegisteredName, string? TradeName, string Tin, string RegisteredAddress,
    TaxRegime TaxRegime, decimal VatRate, string InvoiceTitle, string? PermitNumber, string? SeriesRange, string? PermitDateIssued);

internal sealed class UpdateCompanyProfileRequestValidator : AbstractValidator<UpdateCompanyProfileRequest>
{
    public UpdateCompanyProfileRequestValidator()
    {
        RuleFor(x => x.RegisteredName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TradeName).MaximumLength(200);
        RuleFor(x => x.Tin).NotEmpty().MaximumLength(20);
        RuleFor(x => x.RegisteredAddress).NotEmpty().MaximumLength(500);
        RuleFor(x => x.TaxRegime).IsInEnum();
        RuleFor(x => x.InvoiceTitle).NotEmpty().MaximumLength(50);
        RuleFor(x => x.PermitNumber).MaximumLength(100);
        RuleFor(x => x.SeriesRange).MaximumLength(100);
        RuleFor(x => x.PermitDateIssued).MaximumLength(10);
    }
}

internal static class CompanyProfileEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/company-profile", async (GetCompanyProfileHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(ct)))
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Accountants));

        app.MapPut("/company-profile", async Task<NoContent> (UpdateCompanyProfileRequest b, UpdateCompanyProfileHandler handler, CancellationToken ct) =>
            {
                await handler.HandleAsync(new UpdateCompanyProfileCommand(b.RegisteredName, b.TradeName, b.Tin, b.RegisteredAddress,
                    b.TaxRegime, b.VatRate, b.InvoiceTitle, b.PermitNumber, b.SeriesRange, b.PermitDateIssued), ct);
                return TypedResults.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(InvoicingRoles.Accountants));
    }
}