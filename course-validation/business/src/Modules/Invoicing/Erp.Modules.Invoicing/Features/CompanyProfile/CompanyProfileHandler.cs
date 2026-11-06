using Dapper;
using Erp.Modules.Invoicing.Domain;
using Erp.Modules.Invoicing.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;

namespace Erp.Modules.Invoicing.Features;

internal sealed record GetCompanyProfileQuery : IQuery<CompanyProfileDto>;

internal sealed record CompanyProfileDto(string RegisteredName, string? TradeName, string Tin, string RegisteredAddress, string TaxRegime,
    decimal VatRate, string InvoiceTitle, string? PermitNumber, string? SeriesRange, string? PermitDateIssued, long? UpdatedBy,
    DateTime? UpdatedUtc, bool IsComplete);

internal sealed record CompanyProfileRow(string RegisteredName, string? TradeName, string Tin, string RegisteredAddress, int TaxRegime,
    decimal VatRate, string InvoiceTitle, string? PermitNumber, string? SeriesRange, string? PermitDateIssued, long? UpdatedBy,
    DateTime? UpdatedUtc);

internal sealed class GetCompanyProfileHandler(IDbConnectionFactory connections) : IQueryHandler<GetCompanyProfileQuery, CompanyProfileDto>
{
    public async ValueTask<CompanyProfileDto> Handle(GetCompanyProfileQuery q, CancellationToken ct)
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
    TaxRegime TaxRegime, decimal VatRate, string InvoiceTitle, string? PermitNumber, string? SeriesRange, string? PermitDateIssued)
    : ICommand;

internal sealed class UpdateCompanyProfileValidator : AbstractValidator<UpdateCompanyProfileCommand>
{
    public UpdateCompanyProfileValidator()
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

// Changes apply to invoices issued from now on; every issued invoice keeps the details it was issued with.
internal sealed class UpdateCompanyProfileHandler(InvoicingDbContext db, ICurrentUser user, IClock clock)
    : ICommandHandler<UpdateCompanyProfileCommand>
{
    public async ValueTask<Unit> Handle(UpdateCompanyProfileCommand cmd, CancellationToken ct)
    {
        var profile = await db.CompanyProfileAsync(ct);
        profile.Update(cmd.RegisteredName, cmd.TradeName, cmd.Tin, cmd.RegisteredAddress, new TaxPolicy(cmd.TaxRegime, cmd.VatRate),
            cmd.InvoiceTitle, cmd.PermitNumber, cmd.SeriesRange, cmd.PermitDateIssued, new UserId(user.UserId), clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
