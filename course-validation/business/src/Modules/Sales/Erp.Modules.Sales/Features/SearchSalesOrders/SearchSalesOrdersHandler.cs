using Dapper;
using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;

namespace Erp.Modules.Sales.Features;

internal sealed record SearchSalesOrdersQuery(long? CustomerId, string? Status, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<SalesOrderSummaryDto>>;

internal sealed record SalesOrderSummaryDto(
    long Id, string OrderNumber, string CustomerName, string Status, decimal Total, DateTime CreatedUtc);

// The function returns Status as its stored number (the handler turns it into a name) and, on every row, the
// total number of matches.
internal sealed record SalesOrderSummaryRow(
    long Id, string OrderNumber, string CustomerName, int Status, decimal Total, DateTime CreatedUtc, long TotalCount);

internal sealed class SearchSalesOrdersValidator : AbstractValidator<SearchSalesOrdersQuery>
{
    public SearchSalesOrdersValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Status)
            .Must(s => s is null || Enum.TryParse<SalesOrderStatus>(s, ignoreCase: true, out _))
            .WithMessage(x => $"Unknown status '{x.Status}'.");
    }
}

internal sealed class SearchSalesOrdersHandler(IDbConnectionFactory connections)
    : IQueryHandler<SearchSalesOrdersQuery, PagedResult<SalesOrderSummaryDto>>
{
    public async ValueTask<PagedResult<SalesOrderSummaryDto>> Handle(SearchSalesOrdersQuery q, CancellationToken ct)
    {
        int? status = q.Status is null ? null : (int)Enum.Parse<SalesOrderStatus>(q.Status, ignoreCase: true);

        await using var conn = connections.Create();
        var rows = (await conn.QueryAsync<SalesOrderSummaryRow>(new CommandDefinition(
            "SELECT * FROM sales.search_sales_orders(@CustomerId, @Status, @Page, @PageSize)",
            new { q.CustomerId, Status = status, q.Page, q.PageSize }, cancellationToken: ct))).ToList();
        var total = rows.Count == 0 ? 0 : (int)rows[0].TotalCount;

        var items = rows.Select(r => new SalesOrderSummaryDto(
            r.Id, r.OrderNumber, r.CustomerName, ((SalesOrderStatus)r.Status).ToString(), r.Total, r.CreatedUtc)).ToList();
        return new PagedResult<SalesOrderSummaryDto>(items, q.Page, q.PageSize, total);
    }
}
