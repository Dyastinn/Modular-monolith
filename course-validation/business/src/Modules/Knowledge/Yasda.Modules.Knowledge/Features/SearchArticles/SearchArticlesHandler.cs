using Dapper;
using FluentValidation;
using Yasda.Modules.Knowledge.Domain;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Knowledge.Features;

// The staff list: working copies, whatever their status. Mine: only the articles I wrote or may edit by name.
internal sealed record SearchArticlesQuery(string? Search = null, string? Status = null, string? Visibility = null, bool Mine = false,
    string? Sort = null, bool Desc = false, int Page = 1, int PageSize = 20);

internal sealed record ArticleRowDto(long Id, string Title, string Summary, string Status, string Visibility, int CurrentVersion,
    long CreatedById, string CreatedByName, string LastEditedByName, DateTime UpdatedUtc);

internal sealed class SearchArticlesValidator : AbstractValidator<SearchArticlesQuery>
{
    public SearchArticlesValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
        RuleFor(x => x.Sort).ValidSort("title", "status", "visibility", "author", "updated");
        RuleFor(x => x.Status).IsEnumName(typeof(ArticleStatus), caseSensitive: true).When(x => x.Status is not null);
        RuleFor(x => x.Visibility).IsEnumName(typeof(ArticleVisibility), caseSensitive: true).When(x => x.Visibility is not null);
    }
}

internal sealed class SearchArticlesHandler(IDbConnectionFactory connections, ICurrentUser currentUser)
{
    public async Task<PagedResult<ArticleRowDto>> HandleAsync(SearchArticlesQuery q, CancellationToken ct)
    {
        long? mine = null;
        if (q.Mine) mine = currentUser.UserId;

        var args = new { Search = SearchText.ForLike(q.Search), q.Status, q.Visibility, Mine = mine, q.Sort, q.Desc, q.Page, q.PageSize };

        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM knowledge.search_articles(@Search, @Status, @Visibility, @Mine, @Sort, @Desc, @Page, @PageSize);
            SELECT knowledge.count_articles(@Search, @Status, @Visibility, @Mine);
            """, args, cancellationToken: ct));

        var rows = (await multi.ReadAsync<ArticleRowDto>()).ToList();
        var total = (int)await multi.ReadSingleAsync<long>();
        return new PagedResult<ArticleRowDto>(rows, q.Page, q.PageSize, total);
    }
}
