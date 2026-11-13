using Dapper;
using Erp.Shared.BuildingBlocks;
using FluentValidation;

namespace Erp.Modules.Purchasing.Features;

internal sealed record SearchSuppliersQuery(string? Search = null, bool ActiveOnly = false, int Page = 1, int PageSize = 20);
internal sealed record GetSupplierQuery(long SupplierId);

internal sealed record SupplierDto(long Id, string Name, string? Tin, string? Address, string? Email, string? Phone,
    int PaymentTermsDays, bool IsActive);

internal sealed class SearchSuppliersValidator : AbstractValidator<SearchSuppliersQuery>
{
    public SearchSuppliersValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
    }
}

internal sealed class SearchSuppliersHandler(IDbConnectionFactory connections)
{
    public async Task<PagedResult<SupplierDto>> HandleAsync(SearchSuppliersQuery q, CancellationToken ct)
    {
        var args = new { Search = SearchText.ForLike(q.Search), q.ActiveOnly, q.Page, q.PageSize };
        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM purchasing.search_suppliers(NULL, @Search, @ActiveOnly, @Page, @PageSize);
            SELECT purchasing.count_suppliers(@Search, @ActiveOnly);
            """, args, cancellationToken: ct));

        var rows = (await multi.ReadAsync<SupplierDto>()).ToList();
        return new PagedResult<SupplierDto>(rows, q.Page, q.PageSize, (int)await multi.ReadSingleAsync<long>());
    }

    public async Task<SupplierDto?> HandleAsync(GetSupplierQuery q, CancellationToken ct)
    {
        await using var conn = connections.Create();
        return await conn.QuerySingleOrDefaultAsync<SupplierDto>(new CommandDefinition(
            "SELECT * FROM purchasing.search_suppliers(@SupplierId, NULL, FALSE, 1, 1);", new { q.SupplierId }, cancellationToken: ct));
    }
}
