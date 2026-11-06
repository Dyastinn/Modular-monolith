using Dapper;
using Erp.Modules.Invoicing.Domain;
using Erp.Modules.Invoicing.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Invoicing.Features;

internal sealed record WithholdingSummaryQuery(int Year) : IQuery<WithholdingReportDto>;

internal sealed record WithholdingReportDto(int Year, IReadOnlyList<WithholdingSummaryRow> Summary,
    IReadOnlyList<WithholdingCertificateRow> Certificates);

internal sealed record WithholdingSummaryRow(long CustomerId, string CustomerName, int Quarter, decimal Withheld, decimal Certified,
    decimal Missing);

internal sealed record WithholdingCertificateRow(long Id, long CustomerId, string CustomerName, int Quarter, decimal Amount,
    string? Reference, DateOnly ReceivedOn, long RecordedBy, DateTime RecordedUtc, DateTime? VoidedUtc, string? VoidReason);

internal sealed class WithholdingSummaryValidator : AbstractValidator<WithholdingSummaryQuery>
{
    public WithholdingSummaryValidator() => RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
}

internal sealed class WithholdingSummaryHandler(IDbConnectionFactory connections) : IQueryHandler<WithholdingSummaryQuery, WithholdingReportDto>
{
    public async ValueTask<WithholdingReportDto> Handle(WithholdingSummaryQuery q, CancellationToken ct)
    {
        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM invoicing.withholding_summary(@Year);
            SELECT * FROM invoicing.list_withholding_certificates(@Year);
            """, new { q.Year }, cancellationToken: ct));
        return new WithholdingReportDto(q.Year, (await multi.ReadAsync<WithholdingSummaryRow>()).ToList(),
            (await multi.ReadAsync<WithholdingCertificateRow>()).ToList());
    }
}

internal sealed record RecordWithholdingCertificateCommand(long CustomerId, int Year, int Quarter, decimal Amount, string? Reference,
    DateOnly ReceivedOn) : ICommand<long>;

internal sealed class RecordWithholdingCertificateValidator : AbstractValidator<RecordWithholdingCertificateCommand>
{
    public RecordWithholdingCertificateValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.Amount).GreaterThan(0).Must(a => decimal.Round(a, 2) == a).WithMessage("Amounts are in pesos and centavos.");
        RuleFor(x => x.Reference).MaximumLength(100);
    }
}

internal sealed class RecordWithholdingCertificateHandler(InvoicingDbContext db, ICurrentUser user, IClock clock)
    : ICommandHandler<RecordWithholdingCertificateCommand, long>
{
    public async ValueTask<long> Handle(RecordWithholdingCertificateCommand cmd, CancellationToken ct)
    {
        // The customer's name as billed; a customer we never invoiced can't have withheld anything from us.
        var customerName = await db.Invoices.Where(i => i.CustomerId == cmd.CustomerId)
                .OrderByDescending(i => i.Id).Select(i => i.CustomerName).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"No invoices for customer {cmd.CustomerId}.");

        var certificate = WithholdingCertificate.Record(cmd.CustomerId, customerName, cmd.Year, cmd.Quarter, new Money(cmd.Amount),
            cmd.Reference, cmd.ReceivedOn, new UserId(user.UserId), clock.Today, clock.UtcNow);
        db.WithholdingCertificates.Add(certificate);
        await db.SaveChangesAsync(ct);
        return certificate.Id.Value;
    }
}

internal sealed record VoidWithholdingCertificateCommand(long CertificateId, string Reason) : ICommand;

internal sealed class VoidWithholdingCertificateValidator : AbstractValidator<VoidWithholdingCertificateCommand>
{
    public VoidWithholdingCertificateValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

internal sealed class VoidWithholdingCertificateHandler(InvoicingDbContext db, IClock clock)
    : ICommandHandler<VoidWithholdingCertificateCommand>
{
    public async ValueTask<Unit> Handle(VoidWithholdingCertificateCommand cmd, CancellationToken ct)
    {
        var certificate = await db.WithholdingCertificates.FindAsync([new WithholdingCertificateId(cmd.CertificateId)], ct)
            ?? throw new NotFoundException($"Certificate {cmd.CertificateId} not found.");
        certificate.Void(cmd.Reason, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
