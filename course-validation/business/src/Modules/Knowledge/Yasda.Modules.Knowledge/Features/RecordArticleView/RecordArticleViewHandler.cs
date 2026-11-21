using Yasda.Modules.Knowledge.Infrastructure;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Knowledge.Features;

internal sealed record RecordArticleViewCommand(long ArticleId);

internal sealed class RecordArticleViewHandler(KnowledgeDbContext db, ICurrentUser currentUser, TimeProvider time)
{
    // false when this reader can't see the article (the same test as the reader's article page), so nothing is counted.
    public async Task<bool> HandleAsync(RecordArticleViewCommand cmd, CancellationToken ct)
    {
        var publicOnly = !KnowledgeRoles.IsWriter(currentUser.Role);
        var today = time.Today();

        // One statement that checks and counts: two readers at once both add 1, which a read followed by a write would lose.
        var rows = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO knowledge.article_view_day (article_id, day, views)
            SELECT a.id, {today}, 1
            FROM knowledge.article a
            JOIN knowledge.article_version v ON v.article_id = a.id AND v.number = a.current_version
            WHERE a.id = {cmd.ArticleId} AND a.status <> 'Archived' AND a.current_version > 0
              AND (NOT {publicOnly} OR v.visibility = 'Public')
            ON CONFLICT (article_id, day) DO UPDATE SET views = knowledge.article_view_day.views + 1
            """, ct);
        return rows > 0;
    }
}
