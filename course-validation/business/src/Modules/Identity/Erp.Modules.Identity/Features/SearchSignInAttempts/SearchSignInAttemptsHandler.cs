using Dapper;
using Erp.Shared.BuildingBlocks;
using FluentValidation;

namespace Erp.Modules.Identity.Features;

internal sealed record SearchSignInAttemptsQuery(string? Email = null, bool FailedOnly = false, int Page = 1, int PageSize = 50);

internal sealed record SignInAttemptDto(long Id, string Email, long? UserId, string Outcome, string? IpAddress, DateTime OccurredUtc);

internal sealed class SearchSignInAttemptsValidator : AbstractValidator<SearchSignInAttemptsQuery>
{
    public SearchSignInAttemptsValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
    }
}

internal sealed class SearchSignInAttemptsHandler(IDbConnectionFactory connections)
{
    public async Task<PagedResult<SignInAttemptDto>> HandleAsync(SearchSignInAttemptsQuery q, CancellationToken ct)
    {
        var args = new { Email = SearchText.ForLike(q.Email), q.FailedOnly, q.Page, q.PageSize };
        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM identity.search_sign_in_attempts(@Email, @FailedOnly, @Page, @PageSize);
            SELECT identity.count_sign_in_attempts(@Email, @FailedOnly);
            """, args, cancellationToken: ct));

        var rows = (await multi.ReadAsync<SignInAttemptDto>()).ToList();
        return new PagedResult<SignInAttemptDto>(rows, q.Page, q.PageSize, (int)await multi.ReadSingleAsync<long>());
    }
}
