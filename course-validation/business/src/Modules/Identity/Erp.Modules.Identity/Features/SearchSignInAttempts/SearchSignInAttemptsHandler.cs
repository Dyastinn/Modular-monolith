using Dapper;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;

namespace Erp.Modules.Identity.Features;

internal sealed record SearchSignInAttemptsQuery(string? Email, bool FailedOnly, int Page, int PageSize)
    : IQuery<PagedResult<SignInAttemptDto>>;

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
    : IQueryHandler<SearchSignInAttemptsQuery, PagedResult<SignInAttemptDto>>
{
    public async ValueTask<PagedResult<SignInAttemptDto>> Handle(SearchSignInAttemptsQuery q, CancellationToken ct)
    {
        var args = new { Email = string.IsNullOrWhiteSpace(q.Email) ? null : q.Email.Trim(), q.FailedOnly, q.Page, q.PageSize };
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
