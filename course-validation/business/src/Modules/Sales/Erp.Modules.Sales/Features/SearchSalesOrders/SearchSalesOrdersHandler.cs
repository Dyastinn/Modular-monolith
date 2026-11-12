using Dapper;
using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;

namespace Erp.Modules.Sales.Features;

internal sealed record SearchSalesOrdersQuery(long? CustomerId, string? Status, string? Search, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<SalesOrderSummaryDto>>;

internal sealed record SalesOrderSummaryDto(
    long Id, string OrderNumber, long CustomerId, string CustomerName, string Status, decimal Total, long CreatedBy, DateTime CreatedUtc);

internal sealed record SalesOrderSummaryRow(
    long Id, string OrderNumber, long CustomerId, string CustomerName, int Status, decimal Total, long CreatedBy, DateTime CreatedUtc);

internal sealed class SearchSalesOrdersValidator : AbstractValidator<SearchSalesOrdersQuery>
{
    public SearchSalesOrdersValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
        RuleFor(x => x.Status)
            .Must(s => s is null || Enum.GetNames<SalesOrderStatus>().Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage(x => $"Unknown status '{x.Status}'.");
    }
}

internal sealed class SearchSalesOrdersHandler(IDbConnectionFactory connections, ICurrentUser currentUser)
    : IQueryHandler<SearchSalesOrdersQuery, PagedResult<SalesOrderSummaryDto>>
{
    public async ValueTask<PagedResult<SalesOrderSummaryDto>> Handle(SearchSalesOrdersQuery q, CancellationToken ct)
    {
        int? status = q.Status is null ? null : (int)Enum.Parse<SalesOrderStatus>(q.Status, ignoreCase: true);
        var args = new
        {
            q.CustomerId, Status = status, Search = SearchText.ForLike(q.Search),
            CreatedBy = SalesOrder.CreatorFilterFor(new UserId(currentUser.UserId), currentUser.Role)?.Value,
            q.Page, q.PageSize
        };

        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM sales.search_sales_orders(@CustomerId, @Status, @CreatedBy, @Search, @Page, @PageSize);
            SELECT sales.count_sales_orders(@CustomerId, @Status, @CreatedBy, @Search);
            """, args, cancellationToken: ct));

        var items = (await multi.ReadAsync<SalesOrderSummaryRow>())
            .Select(r => new SalesOrderSummaryDto(r.Id, r.OrderNumber, r.CustomerId, r.CustomerName,
                ((SalesOrderStatus)r.Status).ToString(), r.Total, r.CreatedBy, r.CreatedUtc))
            .ToList();
        return new PagedResult<SalesOrderSummaryDto>(items, q.Page, q.PageSize, (int)await multi.ReadSingleAsync<long>());
    }
}
