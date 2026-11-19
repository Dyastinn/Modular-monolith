using Dapper;
using Yasda.Shared.BuildingBlocks;
using FluentValidation;

namespace Yasda.Modules.Identity.Features;

internal sealed record SearchUsersQuery(string? Search = null, string? Sort = null, bool Desc = false, int Page = 1, int PageSize = 20);

internal sealed record UserDto(
    long Id, string Email, string FullName, string Role, bool IsDisabled, bool MustChangePassword,
    DateTime? LockedOutUntil, DateTime? LastSignIn);

internal sealed class SearchUsersValidator : AbstractValidator<SearchUsersQuery>
{
    public SearchUsersValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
        RuleFor(x => x.Sort).ValidSort("fullName", "email", "role", "lastSignIn");
    }
}

internal sealed class SearchUsersHandler(IDbConnectionFactory connections)
{
    public async Task<PagedResult<UserDto>> HandleAsync(SearchUsersQuery q, CancellationToken ct)
    {
        var search = SearchText.ForLike(q.Search);
        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM identity.search_users(@Search, @Sort, @Desc, @Page, @PageSize);
            SELECT identity.count_users(@Search);
            """, new { Search = search, q.Sort, q.Desc, q.Page, q.PageSize }, cancellationToken: ct));

        var rows = (await multi.ReadAsync<UserDto>()).ToList();
        return new PagedResult<UserDto>(rows, q.Page, q.PageSize, (int)await multi.ReadSingleAsync<long>());
    }
}
