using Dapper;
using Erp.Shared.BuildingBlocks;
using FluentValidation;

namespace Erp.Modules.Identity.Features;

internal sealed record SearchUsersQuery(string? Search = null, int Page = 1, int PageSize = 20);

internal sealed record UserDto(
    long Id, string Email, string FullName, string Role, bool IsDisabled, bool MustChangePassword,
    DateTime? LockedOutUntil, DateTime? LastSignIn);

internal sealed class SearchUsersValidator : AbstractValidator<SearchUsersQuery>
{
    public SearchUsersValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
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
            SELECT * FROM identity.search_users(@Search, @Page, @PageSize);
            SELECT identity.count_users(@Search);
            """, new { Search = search, q.Page, q.PageSize }, cancellationToken: ct));

        var rows = (await multi.ReadAsync<UserDto>()).ToList();
        return new PagedResult<UserDto>(rows, q.Page, q.PageSize, (int)await multi.ReadSingleAsync<long>());
    }
}

// Names for "approved by", "shipped by" and the like. Every signed-in user may read it; it holds no secrets.
internal sealed record UserNameDto(long Id, string FullName);

internal sealed class GetUserDirectoryHandler(IDbConnectionFactory connections)
{
    public async Task<IReadOnlyList<UserNameDto>> HandleAsync(CancellationToken ct)
    {
        await using var conn = connections.Create();
        return (await conn.QueryAsync<UserNameDto>(new CommandDefinition(
            "SELECT * FROM identity.user_directory();", cancellationToken: ct))).ToList();
    }
}
