using Dapper;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;

namespace Erp.Modules.Sales.Features;

internal sealed record SearchPriceListQuery(string? Search, bool ForSaleOnly, int Page, int PageSize) : IQuery<PagedResult<PriceListItemDto>>;

internal sealed record PriceListItemDto(
    long Id, string Sku, string Name, string Unit, bool AllowsFractions, bool IsActive,
    decimal UnitPrice, long? CategoryId, string? CategoryName, bool IsForSale, TaxType TaxType);

internal sealed class SearchPriceListValidator : AbstractValidator<SearchPriceListQuery>
{
    public SearchPriceListValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
    }
}

internal sealed class SearchPriceListHandler(IDbConnectionFactory connections)
    : IQueryHandler<SearchPriceListQuery, PagedResult<PriceListItemDto>>
{
    public async ValueTask<PagedResult<PriceListItemDto>> Handle(SearchPriceListQuery q, CancellationToken ct)
    {
        var args = new { Search = string.IsNullOrWhiteSpace(q.Search) ? null : q.Search.Trim(), q.ForSaleOnly, q.Page, q.PageSize };
        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM sales.search_price_list(@Search, @ForSaleOnly, @Page, @PageSize);
            SELECT sales.count_price_list(@Search, @ForSaleOnly);
            """, args, cancellationToken: ct));

        var rows = (await multi.ReadAsync<PriceListItemDto>()).ToList();
        return new PagedResult<PriceListItemDto>(rows, q.Page, q.PageSize, (int)await multi.ReadSingleAsync<long>());
    }
}
