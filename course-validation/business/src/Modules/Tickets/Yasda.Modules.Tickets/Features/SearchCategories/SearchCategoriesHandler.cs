using Dapper;
using FluentValidation;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Features;

internal sealed record SearchCategoriesQuery(string? Search = null, string? Sort = null, bool Desc = false, int Page = 1, int PageSize = 20);

internal sealed record CategoryDto(long Id, string Name, long? TeamId, string? TeamName, bool IsActive);

internal sealed class SearchCategoriesValidator : AbstractValidator<SearchCategoriesQuery>
{
    public SearchCategoriesValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
        RuleFor(x => x.Sort).ValidSort("name", "team", "status");
    }
}

internal sealed class SearchCategoriesHandler(IDbConnectionFactory connections, ICurrentUser currentUser)
{
    public async Task<PagedResult<CategoryDto>> HandleAsync(SearchCategoriesQuery q, CancellationToken ct)
    {
        // Team leads and admins manage every category. Anyone else, filing a ticket, only ever sees the ones in use.
        var activeOnly = !TicketRoles.Leads.Contains(currentUser.Role);

        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM tickets.search_categories(@Search, @ActiveOnly, @Sort, @Desc, @Page, @PageSize);
            SELECT tickets.count_categories(@Search, @ActiveOnly);
            """, new { Search = SearchText.ForLike(q.Search), ActiveOnly = activeOnly, q.Sort, q.Desc, q.Page, q.PageSize }, cancellationToken: ct));

        var rows = (await multi.ReadAsync<CategoryDto>()).ToList();
        var total = (int)await multi.ReadSingleAsync<long>();
        return new PagedResult<CategoryDto>(rows, q.Page, q.PageSize, total);
    }
}
