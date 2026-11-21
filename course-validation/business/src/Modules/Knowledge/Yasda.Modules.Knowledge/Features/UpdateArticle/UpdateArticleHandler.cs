using Yasda.Modules.Knowledge.Domain;
using Yasda.Modules.Knowledge.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Knowledge.Features;

internal sealed record UpdateArticleCommand(long ArticleId, string Title, string Summary, string Body, ArticleVisibility Visibility);

internal sealed class UpdateArticleHandler(KnowledgeDbContext db, ActingUser actingUser, TimeProvider time)
{
    // false when there is no such article. Whether this person may change it is the article's call.
    public async Task<bool> HandleAsync(UpdateArticleCommand cmd, CancellationToken ct)
    {
        var id = new ArticleId(cmd.ArticleId);
        var article = await db.Articles.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (article is null) return false;

        var person = await actingUser.PersonAsync(ct);
        var access = await actingUser.AccessAsync(article.CreatedById, ct);
        article.Edit(cmd.Title, cmd.Summary, cmd.Body, cmd.Visibility, person, access, time.GetUtcNow().UtcDateTime);

        await db.SaveChangesAsync(ct);
        return true;
    }
}
