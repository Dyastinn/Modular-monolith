using Dapper;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Knowledge.Features;

internal sealed record PopularArticlesQuery;

internal sealed record PopularArticleDto(long Id, string Title, string Summary, long Views);

internal sealed class PopularArticlesHandler(IDbConnectionFactory connections, ICurrentUser currentUser, TimeProvider time)
{
    // The most read articles of the last month, among the ones this reader may see.
    private const int Days = 30;
    private const int HowMany = 5;

    public async Task<IReadOnlyList<PopularArticleDto>> HandleAsync(PopularArticlesQuery q, CancellationToken ct)
    {
        var publicOnly = !KnowledgeRoles.IsWriter(currentUser.Role);
        var since = time.Today().AddDays(-Days);

        await using var conn = connections.Create();
        var rows = await conn.QueryAsync<PopularArticleDto>(new CommandDefinition(
            "SELECT * FROM knowledge.popular_articles(@Since, @PublicOnly, @Limit)",
            new { Since = since, PublicOnly = publicOnly, Limit = HowMany }, cancellationToken: ct));
        return rows.ToList();
    }
}
