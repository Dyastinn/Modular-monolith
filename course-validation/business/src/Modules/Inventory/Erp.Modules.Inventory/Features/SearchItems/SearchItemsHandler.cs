using Dapper;
using Erp.Modules.Inventory.Domain;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;

namespace Erp.Modules.Inventory.Features;

internal sealed record SearchItemsQuery(string? Search, bool ActiveOnly, int Page, int PageSize) : IQuery<PagedResult<ItemDto>>;

internal sealed record ItemDto(
    long Id, string Sku, string Name, string Unit, bool AllowsFractions, bool IsActive,
    decimal OnHand, decimal Reserved, decimal Available);

internal sealed record ItemRow(long Id, string Sku, string Name, string Unit, bool AllowsFractions, bool IsActive, decimal OnHand, decimal Reserved);

internal sealed class SearchItemsValidator : AbstractValidator<SearchItemsQuery>
{
    public SearchItemsValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
    }
}

internal sealed class SearchItemsHandler(IDbConnectionFactory connections) : IQueryHandler<SearchItemsQuery, PagedResult<ItemDto>>
{
    public async ValueTask<PagedResult<ItemDto>> Handle(SearchItemsQuery q, CancellationToken ct)
    {
        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM inventory.search_items(@Search, @ActiveOnly, @Page, @PageSize);
            SELECT inventory.count_items(@Search, @ActiveOnly);
            """,
            new { Search = string.IsNullOrWhiteSpace(q.Search) ? null : q.Search.Trim(), q.ActiveOnly, q.Page, q.PageSize },
            cancellationToken: ct));

        var items = (await multi.ReadAsync<ItemRow>())
            .Select(r => new ItemDto(r.Id, r.Sku, r.Name, r.Unit, r.AllowsFractions, r.IsActive, r.OnHand, r.Reserved, r.OnHand - r.Reserved))
            .ToList();
        return new PagedResult<ItemDto>(items, q.Page, q.PageSize, (int)await multi.ReadSingleAsync<long>());
    }
}
