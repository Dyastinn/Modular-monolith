using Dapper;
using Erp.Modules.Invoicing.Domain;
using Erp.Modules.Invoicing.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.Extensions.Configuration;

namespace Erp.Modules.Invoicing.Features;

internal sealed record GetInvoiceQuery(long InvoiceId) : IQuery<InvoiceDetailDto?>;

internal sealed record InvoiceDetailDto(
    long Id, string Number, long ShipmentId, long SalesOrderId, string OrderNumber, long CustomerId, string CustomerName,
    string? CustomerTin, string? CustomerAddress, DateOnly IssueDate, DateOnly DueDate, string Status,
    decimal Total, decimal AmountPaid, decimal BalanceDue, decimal? Vatable, decimal? Vat,
    string? VoidReason, long? VoidedBy, DateTime? VoidedUtc,
    IReadOnlyList<InvoiceLineDto> Lines, IReadOnlyList<PaymentDto> Payments);

internal sealed record InvoiceLineDto(string Description, string Unit, decimal Quantity, decimal UnitPrice, decimal LineTotal);

internal sealed record PaymentDto(long Id, decimal Amount, DateOnly PaidOn, string Method, string? Reference, long RecordedBy,
    DateTime RecordedUtc, DateTime? VoidedUtc, string? VoidReason);

internal sealed record InvoiceHeaderRow(
    long Id, string Number, long ShipmentId, long SalesOrderId, string OrderNumber, long CustomerId, string CustomerName,
    string? CustomerTin, string? CustomerAddress, DateOnly IssueDate, DateOnly DueDate, int Status, decimal Total,
    decimal AmountPaid, string? VoidReason, long? VoidedBy, DateTime? VoidedUtc);

internal sealed record InvoiceLineRow(string Description, string Unit, decimal Quantity, decimal UnitPrice);

internal sealed record PaymentRow(long Id, decimal Amount, DateOnly PaidOn, int Method, string? Reference, long RecordedBy,
    DateTime RecordedUtc, DateTime? VoidedUtc, string? VoidReason);

internal sealed class GetInvoiceHandler(IDbConnectionFactory connections, IConfiguration config)
    : IQueryHandler<GetInvoiceQuery, InvoiceDetailDto?>
{
    public async ValueTask<InvoiceDetailDto?> Handle(GetInvoiceQuery q, CancellationToken ct)
    {
        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM invoicing.get_invoice(@Id);
            SELECT * FROM invoicing.get_invoice_lines(@Id);
            SELECT * FROM invoicing.get_invoice_payments(@Id);
            """, new { Id = q.InvoiceId }, cancellationToken: ct));

        var h = await multi.ReadSingleOrDefaultAsync<InvoiceHeaderRow>();
        if (h is null) return null;
        var lines = (await multi.ReadAsync<InvoiceLineRow>())
            .Select(l => new InvoiceLineDto(l.Description, l.Unit, l.Quantity, l.UnitPrice,
                Math.Round(l.Quantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero)))
            .ToList();
        var payments = (await multi.ReadAsync<PaymentRow>())
            .Select(p => new PaymentDto(p.Id, p.Amount, p.PaidOn, ((PaymentMethod)p.Method).ToString(), p.Reference,
                p.RecordedBy, p.RecordedUtc, p.VoidedUtc, p.VoidReason))
            .ToList();

        var vatRegistered = config.GetValue("Company:VatRegistered", true);
        var (vatable, vat) = VatBreakdown.Of(new Money(h.Total));
        return new InvoiceDetailDto(h.Id, h.Number, h.ShipmentId, h.SalesOrderId, h.OrderNumber, h.CustomerId, h.CustomerName,
            h.CustomerTin, h.CustomerAddress, h.IssueDate, h.DueDate, ((InvoiceStatus)h.Status).ToString(),
            h.Total, h.AmountPaid, h.Total - h.AmountPaid,
            vatRegistered ? vatable.Amount : null, vatRegistered ? vat.Amount : null,
            h.VoidReason, h.VoidedBy, h.VoidedUtc, lines, payments);
    }
}

internal sealed record GetInvoicePdfQuery(long InvoiceId) : IQuery<(string FileName, byte[] Content)?>;

internal sealed class GetInvoicePdfHandler(ISender sender, IConfiguration config) : IQueryHandler<GetInvoicePdfQuery, (string, byte[])?>
{
    public async ValueTask<(string, byte[])?> Handle(GetInvoicePdfQuery q, CancellationToken ct)
    {
        if (await sender.Send(new GetInvoiceQuery(q.InvoiceId), ct) is not { } i) return null;

        var company = new CompanyInfo(config["Company:Name"] ?? "Your Company", config["Company:Tin"], config["Company:Address"],
            config.GetValue("Company:VatRegistered", true));
        var model = new InvoicePdfModel(i.Number, i.IssueDate, i.DueDate, i.OrderNumber, i.Status, i.CustomerName, i.CustomerTin,
            i.CustomerAddress, i.Lines.Select(l => (l.Description, l.Unit, l.Quantity, l.UnitPrice, l.LineTotal)).ToList(),
            i.Total, i.AmountPaid, i.Vatable ?? 0, i.Vat ?? 0);
        return ($"{i.Number}.pdf", InvoicePdf.Render(model, company));
    }
}
