using Dapper;
using FluentValidation;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Knowledge.Features;

internal sealed record BrowsePublishedArticlesQuery(string? Search = null, string? Sort = null, bool Desc = false, int Page = 1, int PageSize = 20);

internal sealed record PublishedArticleRowDto(long Id, string Title, string Summary, string Visibility, DateTime PublishedUtc);

internal sealed class BrowsePublishedArticlesValidator : AbstractValidator<BrowsePublishedArticlesQuery>
{
    public BrowsePublishedArticlesValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
        RuleFor(x => x.Sort).ValidSort("title", "updated");
    }
}

internal sealed class BrowsePublishedArticlesHandler(IDbConnectionFactory connections, ICurrentUser currentUser)
{
    public async Task<PagedResult<PublishedArticleRowDto>> HandleAsync(BrowsePublishedArticlesQuery q, CancellationToken ct)
    {
        // Staff also read the Internal articles. Anyone else, whatever their role, sees only the Public ones.
        var publicOnly = !KnowledgeRoles.IsWriter(currentUser.Role);

        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM knowledge.search_published_articles(@Search, @PublicOnly, @Sort, @Desc, @Page, @PageSize);
            SELECT knowledge.count_published_articles(@Search, @PublicOnly);
            """, new { Search = SearchText.ForLike(q.Search), PublicOnly = publicOnly, q.Sort, q.Desc, q.Page, q.PageSize }, cancellationToken: ct));

        var rows = (await multi.ReadAsync<PublishedArticleRowDto>()).ToList();
        var total = (int)await multi.ReadSingleAsync<long>();
        return new PagedResult<PublishedArticleRowDto>(rows, q.Page, q.PageSize, total);
    }
}
