using Dapper;
using Erp.Modules.Inventory.Domain;
using Erp.Shared.BuildingBlocks;
using FluentValidation;

namespace Erp.Modules.Inventory.Features;

internal sealed record SearchItemsQuery(string? Search = null, bool ActiveOnly = false, string? Sort = null, bool Desc = false,
    int Page = 1, int PageSize = 20);

internal sealed record ItemDto(
    long Id, string Sku, string Name, string Unit, bool AllowsFractions, bool IsActive,
    decimal OnHand, decimal Reserved, decimal Available, decimal? AverageCost, decimal? StockValue);

internal static class CostViewers
{
    // Cost is the margin's other half: the people who buy, count, bill and run the business see it; sales reps don't.
    public static readonly string[] Roles =
        [Erp.Shared.BuildingBlocks.Roles.InventoryClerk, Erp.Shared.BuildingBlocks.Roles.Purchaser,
         Erp.Shared.BuildingBlocks.Roles.Accountant, Erp.Shared.BuildingBlocks.Roles.SalesManager, Erp.Shared.BuildingBlocks.Roles.Admin];

    public static bool Includes(string role) => Roles.Contains(role);
}

// What everything on hand is worth, at average cost.
internal sealed record StockValuationDto(long ItemCount, decimal TotalValue);

internal sealed class SearchItemsValidator : AbstractValidator<SearchItemsQuery>
{
    public SearchItemsValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
        RuleFor(x => x.Sort).ValidSort("sku", "name", "onHand", "reserved", "available", "averageCost", "stockValue");
    }
}

internal sealed class SearchItemsHandler(IDbConnectionFactory connections, ICurrentUser currentUser)
{
    public async Task<PagedResult<ItemDto>> HandleAsync(SearchItemsQuery q, CancellationToken ct)
    {
        var showCost = CostViewers.Includes(currentUser.Role);
        // Sorting by cost would rank items by it: the order alone gives the secret away.
        if (!showCost && q.Sort is "averageCost" or "stockValue") q = q with { Sort = null };
        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM inventory.search_items(@Search, @ActiveOnly, @Sort, @Desc, @Page, @PageSize);
            SELECT inventory.count_items(@Search, @ActiveOnly);
            """,
            new { Search = SearchText.ForLike(q.Search), q.ActiveOnly, q.Sort, q.Desc, q.Page, q.PageSize },
            cancellationToken: ct));

        var items = (await multi.ReadAsync<ItemDto>())
            .Select(i => showCost ? i : i with { AverageCost = null, StockValue = null })
            .ToList();
        return new PagedResult<ItemDto>(items, q.Page, q.PageSize, (int)await multi.ReadSingleAsync<long>());
    }

    public async Task<StockValuationDto> HandleAsync(CancellationToken ct)
    {
        await using var conn = connections.Create();
        return await conn.QuerySingleAsync<StockValuationDto>(new CommandDefinition(
            "SELECT * FROM inventory.stock_valuation();", cancellationToken: ct));
    }
}
