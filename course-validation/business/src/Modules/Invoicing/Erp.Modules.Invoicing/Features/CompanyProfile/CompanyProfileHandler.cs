using Dapper;
using Erp.Modules.Invoicing.Domain;
using Erp.Modules.Invoicing.Infrastructure;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Invoicing.Features;

internal sealed record CompanyProfileDto(string RegisteredName, string? TradeName, string Tin, string RegisteredAddress, string TaxRegime,
    decimal VatRate, string InvoiceTitle, string? PermitNumber, string? SeriesRange, string? PermitDateIssued, long? UpdatedBy,
    DateTime? UpdatedUtc, bool IsComplete);

internal sealed record CompanyProfileRow(string RegisteredName, string? TradeName, string Tin, string RegisteredAddress, int TaxRegime,
    decimal VatRate, string InvoiceTitle, string? PermitNumber, string? SeriesRange, string? PermitDateIssued, long? UpdatedBy,
    DateTime? UpdatedUtc);

internal sealed class GetCompanyProfileHandler(IDbConnectionFactory connections)
{
    public async Task<CompanyProfileDto> HandleAsync(CancellationToken ct)
    {
        await using var conn = connections.Create();
        var p = await conn.QuerySingleAsync<CompanyProfileRow>(
            new CommandDefinition("SELECT * FROM invoicing.get_company_profile()", cancellationToken: ct));
        return new CompanyProfileDto(p.RegisteredName, p.TradeName, p.Tin, p.RegisteredAddress, ((TaxRegime)p.TaxRegime).ToString(),
            p.VatRate, p.InvoiceTitle, p.PermitNumber, p.SeriesRange, p.PermitDateIssued, p.UpdatedBy, p.UpdatedUtc,
            p.RegisteredName.Length > 0 && p.Tin.Length > 0 && p.RegisteredAddress.Length > 0);
    }
}

internal sealed record UpdateCompanyProfileCommand(string RegisteredName, string? TradeName, string Tin, string RegisteredAddress,
    TaxRegime TaxRegime, decimal VatRate, string InvoiceTitle, string? PermitNumber, string? SeriesRange, string? PermitDateIssued);

// Changes apply to invoices issued from now on; every issued invoice keeps the details it was issued with.
internal sealed class UpdateCompanyProfileHandler(InvoicingDbContext db, ICurrentUser user, TimeProvider time)
{
    public async Task HandleAsync(UpdateCompanyProfileCommand cmd, CancellationToken ct)
    {
        var profile = await db.CompanyProfileAsync(ct);
        profile.Update(cmd.RegisteredName, cmd.TradeName, cmd.Tin, cmd.RegisteredAddress, new TaxPolicy(cmd.TaxRegime, cmd.VatRate),
            cmd.InvoiceTitle, cmd.PermitNumber, cmd.SeriesRange, cmd.PermitDateIssued, new UserId(user.UserId), time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
    }
}
