using Dapper;
using Erp.Modules.Invoicing.Domain;
using Erp.Shared.BuildingBlocks;
using FluentValidation;

namespace Erp.Modules.Invoicing.Features;

internal sealed record SearchInvoicesQuery(long? CustomerId = null, string? Status = null, string? Search = null, bool OverdueOnly = false,
    int Page = 1, int PageSize = 20);

internal sealed record InvoiceSummaryDto(long Id, string Number, long CustomerId, string CustomerName, string OrderNumber,
    DateOnly IssueDate, DateOnly DueDate, string Status, decimal Total, decimal AmountPaid, decimal BalanceDue);

internal sealed record InvoiceSummaryRow(long Id, string Number, long CustomerId, string CustomerName, string OrderNumber,
    DateOnly IssueDate, DateOnly DueDate, int Status, decimal Total, decimal AmountPaid);

internal sealed class SearchInvoicesValidator : AbstractValidator<SearchInvoicesQuery>
{
    public SearchInvoicesValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
        RuleFor(x => x.Status).Must(s => s is null || Enum.GetNames<InvoiceStatus>().Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage(x => $"Unknown status '{x.Status}'.");
    }
}

internal sealed class SearchInvoicesHandler(IDbConnectionFactory connections, TimeProvider time)
{
    public async Task<PagedResult<InvoiceSummaryDto>> HandleAsync(SearchInvoicesQuery q, CancellationToken ct)
    {
        var args = new
        {
            q.CustomerId,
            Status = q.Status is null ? (int?)null : (int)Enum.Parse<InvoiceStatus>(q.Status, true),
            Search = SearchText.ForLike(q.Search),
            OverdueOn = q.OverdueOnly ? time.Today() : (DateOnly?)null,
            q.Page, q.PageSize
        };
        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM invoicing.search_invoices(@CustomerId, @Status, @Search, @OverdueOn, @Page, @PageSize);
            SELECT invoicing.count_invoices(@CustomerId, @Status, @Search, @OverdueOn);
            """, args, cancellationToken: ct));

        var rows = (await multi.ReadAsync<InvoiceSummaryRow>())
            .Select(r => new InvoiceSummaryDto(r.Id, r.Number, r.CustomerId, r.CustomerName, r.OrderNumber, r.IssueDate, r.DueDate,
                ((InvoiceStatus)r.Status).ToString(), r.Total, r.AmountPaid, r.Total - r.AmountPaid))
            .ToList();
        return new PagedResult<InvoiceSummaryDto>(rows, q.Page, q.PageSize, (int)await multi.ReadSingleAsync<long>());
    }
}
