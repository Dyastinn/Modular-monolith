using Dapper;
using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;
using FluentValidation;

namespace Erp.Modules.Sales.Features;

// AwaitingMe: only submitted orders waiting for an approval the caller's role may give, and not their own.
internal sealed record SearchSalesOrdersQuery(long? CustomerId = null, string? Status = null, string? Search = null,
    bool AwaitingMe = false, string? Sort = null, bool Desc = false, int Page = 1, int PageSize = 20);

internal sealed record SalesOrderSummaryDto(
    long Id, string OrderNumber, long CustomerId, string CustomerName, string Status, decimal Total, long CreatedBy, DateTime CreatedUtc,
    DateTime? SubmittedUtc, IReadOnlyList<string> PendingApprovals);

internal sealed record SalesOrderSummaryRow(
    long Id, string OrderNumber, long CustomerId, string CustomerName, int Status, decimal Total, long CreatedBy, DateTime CreatedUtc,
    DateTime? SubmittedUtc, int PendingApprovals);

internal sealed class SearchSalesOrdersValidator : AbstractValidator<SearchSalesOrdersQuery>
{
    public SearchSalesOrdersValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
        RuleFor(x => x.Sort).ValidSort("orderNumber", "customerName", "status", "total", "submittedUtc", "createdUtc");
        RuleFor(x => x.Status)
            .Must(s => s is null || Enum.GetNames<SalesOrderStatus>().Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage(x => $"Unknown status '{x.Status}'.");
    }
}

internal sealed class SearchSalesOrdersHandler(IDbConnectionFactory connections, ICurrentUser currentUser)
{
    public async Task<PagedResult<SalesOrderSummaryDto>> HandleAsync(SearchSalesOrdersQuery q, CancellationToken ct)
    {
        int? status = q.Status is null ? null : (int)Enum.Parse<SalesOrderStatus>(q.Status, ignoreCase: true);
        var me = new UserId(currentUser.UserId);
        var args = new
        {
            q.CustomerId, Status = status, Search = SearchText.ForLike(q.Search),
            CreatedBy = SalesOrder.CreatorFilterFor(me, currentUser.Role)?.Value,
            Awaiting = q.AwaitingMe ? (int?)ApprovalNeedsRules.ClearableBy(currentUser.Role) : null,
            ExcludeCreator = q.AwaitingMe ? (long?)me.Value : null,
            q.Sort, q.Desc, q.Page, q.PageSize
        };

        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM sales.search_sales_orders(@CustomerId, @Status, @CreatedBy, @Search, @Awaiting, @ExcludeCreator, @Sort, @Desc, @Page, @PageSize);
            SELECT sales.count_sales_orders(@CustomerId, @Status, @CreatedBy, @Search, @Awaiting, @ExcludeCreator);
            """, args, cancellationToken: ct));

        var items = (await multi.ReadAsync<SalesOrderSummaryRow>())
            .Select(r => new SalesOrderSummaryDto(r.Id, r.OrderNumber, r.CustomerId, r.CustomerName,
                ((SalesOrderStatus)r.Status).ToString(), r.Total, r.CreatedBy, r.CreatedUtc, r.SubmittedUtc,
                ((ApprovalNeeds)r.PendingApprovals).Names()))
            .ToList();
        return new PagedResult<SalesOrderSummaryDto>(items, q.Page, q.PageSize, (int)await multi.ReadSingleAsync<long>());
    }
}
