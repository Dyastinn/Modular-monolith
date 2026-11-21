using Dapper;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Knowledge.Features;

internal sealed record GetPublishedArticleQuery(long ArticleId);

internal sealed record PublishedArticleDto(long Id, string Title, string Summary, string Body, string Visibility, int VersionNumber, DateTime PublishedUtc);

internal sealed class GetPublishedArticleHandler(IDbConnectionFactory connections, ICurrentUser currentUser)
{
    // null when there is no such article, and when this reader may not see it (a draft, an archived one, an Internal one for a
    // requester): they can't even learn that it exists.
    public async Task<PublishedArticleDto?> HandleAsync(GetPublishedArticleQuery q, CancellationToken ct)
    {
        var publicOnly = !KnowledgeRoles.IsWriter(currentUser.Role);

        await using var conn = connections.Create();
        return await conn.QuerySingleOrDefaultAsync<PublishedArticleDto>(new CommandDefinition(
            "SELECT * FROM knowledge.get_published_article(@ArticleId, @PublicOnly)",
            new { q.ArticleId, PublicOnly = publicOnly }, cancellationToken: ct));
    }
}
