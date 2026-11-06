using Dapper;
using Erp.Modules.Invoicing.Domain;
using Erp.Modules.Invoicing.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Mediator;

namespace Erp.Modules.Invoicing.Features;

internal sealed record GetInvoiceQuery(long InvoiceId) : IQuery<InvoiceDetailDto?>;

internal sealed record InvoiceDetailDto(
    long Id, string Number, InvoiceSellerDto Seller, long ShipmentId, long SalesOrderId, string OrderNumber, long CustomerId, string CustomerName,
    string? CustomerTin, string? CustomerAddress, DateOnly IssueDate, DateOnly DueDate, string Status,
    decimal Total, decimal AmountPaid, decimal BalanceDue,
    string TaxRegime, decimal VatRate, decimal VatableSales, decimal VatAmount, decimal ZeroRatedSales, decimal VatExemptSales,
    int PrintCount, string? VoidReason, long? VoidedBy, DateTime? VoidedUtc, InvoiceSpecialDiscountDto? SpecialDiscount,
    IReadOnlyList<InvoiceLineDto> Lines, IReadOnlyList<PaymentDto> Payments)
{
    public decimal GrossSales => Total + (SpecialDiscount is { } d ? d.VatExemption + d.Discount : 0);
}

internal sealed record InvoiceSellerDto(string Name, string? TradeName, string Tin, string Address, string InvoiceTitle,
    string? PermitNumber, string? SeriesRange, string? PermitDateIssued);

internal sealed record InvoiceSpecialDiscountDto(string Program, string IdLabel, string HolderName, string IdNumber, decimal Rate,
    decimal VatRateRemoved, decimal VatExemption, decimal Discount);

internal sealed record InvoiceLineDto(string Description, string Unit, decimal Quantity, decimal UnitPrice, decimal LineTotal, string TaxType,
    bool SpecialDiscountEligible);

internal sealed record PaymentDto(long Id, decimal Amount, decimal TaxWithheld, DateOnly PaidOn, string Method, string? Reference, long RecordedBy,
    DateTime RecordedUtc, DateTime? VoidedUtc, string? VoidReason);

internal sealed record InvoiceHeaderRow(
    long Id, string Number, long ShipmentId, long SalesOrderId, string OrderNumber, long CustomerId, string CustomerName,
    string? CustomerTin, string? CustomerAddress, DateOnly IssueDate, DateOnly DueDate, int Status, decimal Total,
    decimal AmountPaid, string? VoidReason, long? VoidedBy, DateTime? VoidedUtc,
    int TaxRegime, decimal VatRate, decimal VatableSales, decimal VatAmount, decimal ZeroRatedSales, decimal VatExemptSales, int PrintCount,
    string? SpecialDiscountProgram, string? SpecialDiscountIdLabel, string? SpecialDiscountHolderName, string? SpecialDiscountIdNumber,
    decimal? SpecialDiscountRate, decimal? SpecialDiscountVatRateRemoved, decimal SpecialDiscountVatExemption, decimal SpecialDiscountAmount,
    string SellerName, string? SellerTradeName, string SellerTin, string SellerAddress, string SellerInvoiceTitle,
    string? SellerPermitNumber, string? SellerSeriesRange, string? SellerPermitDateIssued);

internal sealed record InvoiceLineRow(string Description, string Unit, decimal Quantity, decimal UnitPrice, int TaxType,
    bool SpecialDiscountEligible);

internal sealed record PaymentRow(long Id, decimal Amount, decimal TaxWithheld, DateOnly PaidOn, int Method, string? Reference, long RecordedBy,
    DateTime RecordedUtc, DateTime? VoidedUtc, string? VoidReason);

internal sealed class GetInvoiceHandler(IDbConnectionFactory connections) : IQueryHandler<GetInvoiceQuery, InvoiceDetailDto?>
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
                Math.Round(l.Quantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero), ((TaxType)l.TaxType).ToString(),
                l.SpecialDiscountEligible))
            .ToList();
        var payments = (await multi.ReadAsync<PaymentRow>())
            .Select(p => new PaymentDto(p.Id, p.Amount, p.TaxWithheld, p.PaidOn, ((PaymentMethod)p.Method).ToString(), p.Reference,
                p.RecordedBy, p.RecordedUtc, p.VoidedUtc, p.VoidReason))
            .ToList();

        // The tax figures were fixed when the invoice was issued; they never follow later changes to the settings.
        return new InvoiceDetailDto(h.Id, h.Number,
            new InvoiceSellerDto(h.SellerName, h.SellerTradeName, h.SellerTin, h.SellerAddress, h.SellerInvoiceTitle,
                h.SellerPermitNumber, h.SellerSeriesRange, h.SellerPermitDateIssued), h.ShipmentId, h.SalesOrderId, h.OrderNumber, h.CustomerId, h.CustomerName,
            h.CustomerTin, h.CustomerAddress, h.IssueDate, h.DueDate, ((InvoiceStatus)h.Status).ToString(),
            h.Total, h.AmountPaid, h.Total - h.AmountPaid,
            ((TaxRegime)h.TaxRegime).ToString(), h.VatRate, h.VatableSales, h.VatAmount, h.ZeroRatedSales, h.VatExemptSales,
            h.PrintCount, h.VoidReason, h.VoidedBy, h.VoidedUtc,
            h.SpecialDiscountProgram is null ? null : new InvoiceSpecialDiscountDto(h.SpecialDiscountProgram, h.SpecialDiscountIdLabel!,
                h.SpecialDiscountHolderName!, h.SpecialDiscountIdNumber!, h.SpecialDiscountRate ?? 0, h.SpecialDiscountVatRateRemoved ?? 0,
                h.SpecialDiscountVatExemption, h.SpecialDiscountAmount),
            lines, payments);
    }
}

// Printing is recorded, because every copy after the first must say "REPRINT" (RR 7-2024 Sec. 6(B.21)).
internal sealed record PrintInvoiceCommand(long InvoiceId) : ICommand<(string FileName, byte[] Content)?>;

internal sealed class PrintInvoiceHandler(InvoicingDbContext db, ISender sender)
    : ICommandHandler<PrintInvoiceCommand, (string, byte[])?>
{
    public async ValueTask<(string, byte[])?> Handle(PrintInvoiceCommand cmd, CancellationToken ct)
    {
        if (await db.RecordPrintAsync(cmd.InvoiceId, ct) is not { } printCount) return null;
        if (await sender.Send(new GetInvoiceQuery(cmd.InvoiceId), ct) is not { } invoice) return null;
        return ($"{invoice.Number}.pdf", InvoicePdf.Render(invoice, reprint: printCount > 1));
    }
}
