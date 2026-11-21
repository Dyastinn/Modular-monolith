using Yasda.Modules.Knowledge.Domain;
using Yasda.Modules.Knowledge.Infrastructure;

namespace Yasda.Modules.Knowledge.Features;

internal sealed record CreateArticleCommand(string Title, string Summary, string Body, ArticleVisibility Visibility);

internal sealed class CreateArticleHandler(KnowledgeDbContext db, ActingUser actingUser, TimeProvider time)
{
    public async Task<long> HandleAsync(CreateArticleCommand cmd, CancellationToken ct)
    {
        var author = await actingUser.PersonAsync(ct);
        var article = Article.Create(cmd.Title, cmd.Summary, cmd.Body, cmd.Visibility, author, time.GetUtcNow().UtcDateTime);

        db.Articles.Add(article);
        await db.SaveChangesAsync(ct);
        return article.Id.Value;
    }
}
