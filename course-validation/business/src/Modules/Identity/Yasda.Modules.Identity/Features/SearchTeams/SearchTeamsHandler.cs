using Dapper;
using FluentValidation;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Identity.Features;

internal sealed record SearchTeamsQuery(string? Search = null, string? Sort = null, bool Desc = false, int Page = 1, int PageSize = 20);

internal sealed record TeamSummaryDto(long Id, string Name, bool IsActive, string NotifyMode, long MemberCount);

internal sealed class SearchTeamsValidator : AbstractValidator<SearchTeamsQuery>
{
    public SearchTeamsValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
        RuleFor(x => x.Sort).ValidSort("name", "members");
    }
}

internal sealed class SearchTeamsHandler(IDbConnectionFactory connections)
{
    public async Task<PagedResult<TeamSummaryDto>> HandleAsync(SearchTeamsQuery q, CancellationToken ct)
    {
        var search = SearchText.ForLike(q.Search);
        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM identity.search_teams(@Search, @Sort, @Desc, @Page, @PageSize);
            SELECT identity.count_teams(@Search);
            """, new { Search = search, q.Sort, q.Desc, q.Page, q.PageSize }, cancellationToken: ct));

        var rows = (await multi.ReadAsync<TeamSummaryDto>()).ToList();
        var total = (int)await multi.ReadSingleAsync<long>();
        return new PagedResult<TeamSummaryDto>(rows, q.Page, q.PageSize, total);
    }
}
