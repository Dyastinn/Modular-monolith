using Dapper;
using Erp.Modules.Invoicing.Domain;
using Erp.Modules.Invoicing.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;

namespace Erp.Modules.Invoicing.Features;

// RefundPending: only memos with money still to give back.
internal sealed record SearchCreditMemosQuery(long? CustomerId = null, string? Search = null, bool RefundPending = false,
    string? Sort = null, bool Desc = false, int Page = 1, int PageSize = 20);

internal sealed record CreditMemoSummaryDto(long Id, string Number, long InvoiceId, string InvoiceNumber, long CustomerId, string CustomerName,
    DateOnly IssueDate, string Reason, decimal Total, decimal AppliedToInvoice, decimal RefundDue, DateOnly? RefundedOn, bool ReturnToStock);

internal sealed class SearchCreditMemosValidator : AbstractValidator<SearchCreditMemosQuery>
{
    public SearchCreditMemosValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
        RuleFor(x => x.Sort).ValidSort("number", "customerName", "issueDate", "total", "refundDue");
    }
}

internal sealed record GetCreditMemoQuery(long CreditMemoId);

internal sealed record CreditMemoLineDto(string Description, string Unit, decimal Quantity, decimal UnitPrice, decimal LineTotal, string TaxType);

internal sealed record CreditMemoDetailDto(long Id, string Number, InvoiceSellerDto Seller, long InvoiceId, string InvoiceNumber, long CustomerId,
    string CustomerName, string? CustomerTin, string? CustomerAddress, DateOnly IssueDate, string Reason, bool ReturnToStock, string TaxRegime,
    decimal VatRate, decimal VatableSales, decimal VatAmount, decimal ZeroRatedSales, decimal VatExemptSales, decimal SpecialDiscountVatExemption,
    decimal SpecialDiscountAmount, decimal Total, decimal AppliedToInvoice, decimal RefundDue, DateOnly? RefundedOn, string? RefundMethod,
    string? RefundReference, long CreatedBy, DateTime CreatedUtc, int PrintCount, IReadOnlyList<CreditMemoLineDto> Lines);

internal sealed record CreditMemoHeaderRow(long Id, string Number, long InvoiceId, string InvoiceNumber, long CustomerId, string CustomerName,
    string? CustomerTin, string? CustomerAddress, DateOnly IssueDate, string Reason, bool ReturnToStock, int TaxRegime, decimal VatRate,
    decimal VatableSales, decimal VatAmount, decimal ZeroRatedSales, decimal VatExemptSales, decimal SpecialDiscountVatExemption,
    decimal SpecialDiscountAmount, decimal Total, decimal AppliedToInvoice, decimal RefundDue, DateOnly? RefundedOn, int? RefundMethod,
    string? RefundReference, long CreatedBy, DateTime CreatedUtc, int PrintCount, string SellerName, string? SellerTradeName, string SellerTin,
    string SellerAddress, string SellerInvoiceTitle, string? SellerPermitNumber, string? SellerSeriesRange, string? SellerPermitDateIssued);

internal sealed record CreditMemoLineRow(string Description, string Unit, decimal Quantity, decimal UnitPrice, int TaxType);

internal sealed class SearchCreditMemosHandler(IDbConnectionFactory connections)
{
    public async Task<PagedResult<CreditMemoSummaryDto>> HandleAsync(SearchCreditMemosQuery q, CancellationToken ct)
    {
        var args = new { q.CustomerId, Search = SearchText.ForLike(q.Search), q.RefundPending, q.Sort, q.Desc, q.Page, q.PageSize };
        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM invoicing.search_credit_memos(@CustomerId, @Search, @RefundPending, @Sort, @Desc, @Page, @PageSize);
            SELECT invoicing.count_credit_memos(@CustomerId, @Search, @RefundPending);
            """, args, cancellationToken: ct));
        var rows = (await multi.ReadAsync<CreditMemoSummaryDto>()).ToList();
        return new PagedResult<CreditMemoSummaryDto>(rows, q.Page, q.PageSize, (int)await multi.ReadSingleAsync<long>());
    }

    public async Task<CreditMemoDetailDto?> HandleAsync(GetCreditMemoQuery q, CancellationToken ct)
    {
        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM invoicing.get_credit_memo(@Id);
            SELECT * FROM invoicing.get_credit_memo_lines(@Id);
            """, new { Id = q.CreditMemoId }, cancellationToken: ct));
        var h = await multi.ReadSingleOrDefaultAsync<CreditMemoHeaderRow>();
        if (h is null) return null;
        var lines = (await multi.ReadAsync<CreditMemoLineRow>())
            .Select(l => new CreditMemoLineDto(l.Description, l.Unit, l.Quantity, l.UnitPrice,
                Math.Round(l.Quantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero), ((TaxType)l.TaxType).ToString()))
            .ToList();
        return new CreditMemoDetailDto(h.Id, h.Number,
            new InvoiceSellerDto(h.SellerName, h.SellerTradeName, h.SellerTin, h.SellerAddress, h.SellerInvoiceTitle, h.SellerPermitNumber,
                h.SellerSeriesRange, h.SellerPermitDateIssued),
            h.InvoiceId, h.InvoiceNumber, h.CustomerId, h.CustomerName, h.CustomerTin, h.CustomerAddress, h.IssueDate, h.Reason, h.ReturnToStock,
            ((TaxRegime)h.TaxRegime).ToString(), h.VatRate, h.VatableSales, h.VatAmount, h.ZeroRatedSales, h.VatExemptSales,
            h.SpecialDiscountVatExemption, h.SpecialDiscountAmount, h.Total, h.AppliedToInvoice, h.RefundDue, h.RefundedOn,
            h.RefundMethod is { } m ? ((PaymentMethod)m).ToString() : null, h.RefundReference, h.CreatedBy, h.CreatedUtc, h.PrintCount, lines);
    }
}

// Printing is counted, like an invoice's: every copy after the first says "REPRINT".
internal sealed record PrintCreditMemoCommand(long CreditMemoId);

internal sealed class PrintCreditMemoHandler(InvoicingDbContext db, SearchCreditMemosHandler memos)
{
    public async Task<InvoicePdfFile?> HandleAsync(PrintCreditMemoCommand cmd, CancellationToken ct)
    {
        if (await db.RecordCreditMemoPrintAsync(cmd.CreditMemoId, ct) is not { } printCount) return null;
        if (await memos.HandleAsync(new GetCreditMemoQuery(cmd.CreditMemoId), ct) is not { } memo) return null;
        return new InvoicePdfFile($"{memo.Number}.pdf", CreditMemoPdf.Render(memo, reprint: printCount > 1));
    }
}
