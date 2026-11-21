using Yasda.Modules.Knowledge.Domain;
using Yasda.Modules.Knowledge.Infrastructure;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Knowledge.Features;

// The steps of an article's review. Which step is allowed from which status is the article's call.
internal enum ArticleStep { Submit, Reject, Publish, Archive, Restore }

internal sealed record ChangeArticleStatusCommand(long ArticleId, ArticleStep Step, string? Reason = null);

internal sealed class ChangeArticleStatusHandler(KnowledgeDbContext db, ActingUser actingUser, ICurrentUser currentUser, TimeProvider time)
{
    public async Task<bool> HandleAsync(ChangeArticleStatusCommand cmd, CancellationToken ct)
    {
        var id = new ArticleId(cmd.ArticleId);
        var article = await db.Articles.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (article is null) return false;

        var person = await actingUser.PersonAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        switch (cmd.Step)
        {
            case ArticleStep.Submit:
                var access = await actingUser.AccessAsync(article.CreatedById, ct);
                article.Submit(person, access, now);
                break;
            case ArticleStep.Reject:
                article.Reject(cmd.Reason ?? "", person, now);
                break;
            case ArticleStep.Publish:
                article.Publish(person, actorIsAdmin: currentUser.Role == Roles.Admin, now);
                break;
            case ArticleStep.Archive:
                article.Archive(person, now);
                break;
            case ArticleStep.Restore:
                article.Restore(person, now);
                break;
        }

        await db.SaveChangesAsync(ct);
        return true;
    }
}
