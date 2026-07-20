using System.Data;
using Dapper;
using Erp.Shared.BuildingBlocks;
using Erp.Modules.Sales.Domain;
using FluentValidation;

namespace Erp.Modules.Sales.Features;

internal sealed record SearchSalesOrdersQuery(
    long? CustomerId, string? Status, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<SalesOrderSummaryDto>>;

internal sealed record SalesOrderSummaryDto(
    long Id, string OrderNumber, string CustomerName, string Status, decimal Total, DateTime CreatedUtc);

// The proc returns Status as its stored int; the handler converts it to a readable name.
internal sealed record SalesOrderSummaryRow(
    long Id, string OrderNumber, string CustomerName, int Status, decimal Total, DateTime CreatedUtc);

internal sealed class SearchSalesOrdersValidator : AbstractValidator<SearchSalesOrdersQuery>
{
    public SearchSalesOrdersValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);   // cap page size
    }
}

internal sealed class SearchSalesOrdersHandler(IDbConnectionFactory factory)
    : IQueryHandler<SearchSalesOrdersQuery, PagedResult<SalesOrderSummaryDto>>
{
    public async Task<PagedResult<SalesOrderSummaryDto>> Handle(SearchSalesOrdersQuery q, CancellationToken ct)
    {
        // Translate a status name to its number for the proc (null = no filter).
        int? statusNum = null;
        if (q.Status is not null)
        {
            if (!Enum.TryParse<SalesOrderStatus>(q.Status, ignoreCase: true, out var parsed))
                throw new ValidationException($"Unknown status '{q.Status}'.");
            statusNum = (int)parsed;
        }

        using var conn = factory.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            "sales.usp_SalesOrders_Search",
            new { q.CustomerId, Status = statusNum, q.Page, q.PageSize },
            commandType: CommandType.StoredProcedure, cancellationToken: ct));

        var rows = (await multi.ReadAsync<SalesOrderSummaryRow>()).ToList();
        var total = await multi.ReadSingleAsync<int>();   // second result set: total row count

        var items = rows.Select(r => new SalesOrderSummaryDto(
            r.Id, r.OrderNumber, r.CustomerName, ((SalesOrderStatus)r.Status).ToString(), r.Total, r.CreatedUtc)).ToList();

        return new PagedResult<SalesOrderSummaryDto>(items, q.Page, q.PageSize, total);
    }
}
