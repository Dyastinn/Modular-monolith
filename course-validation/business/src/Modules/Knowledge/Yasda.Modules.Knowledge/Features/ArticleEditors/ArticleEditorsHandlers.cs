using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Knowledge.Domain;
using Yasda.Modules.Knowledge.Infrastructure;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Knowledge.Features;

internal sealed record AddArticleEditorCommand(long ArticleId, long UserId);

internal sealed record RemoveArticleEditorCommand(long ArticleId, long UserId);

// The article's own edit list: people who may edit this one article without being its author. Who may change the list is the article's call.
internal sealed class AddArticleEditorHandler(KnowledgeDbContext db, ActingUser actingUser, IUserDirectory users, TimeProvider time)
{
    // false when there is no such article.
    public async Task<bool> HandleAsync(AddArticleEditorCommand cmd, CancellationToken ct)
    {
        var id = new ArticleId(cmd.ArticleId);
        var article = await db.Articles.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (article is null) return false;

        // Only Identity knows who is an active member of staff. An id in the body that isn't one is refused with its code.
        var staff = await users.FindStaffAsync(cmd.UserId, ct);
        if (staff is null)
            throw new DomainException(KnowledgeErrors.EditorNotStaff, $"User {cmd.UserId} isn't an active agent, team lead or admin.");

        var person = await actingUser.PersonAsync(ct);
        var access = await actingUser.AccessAsync(article.CreatedById, ct);
        article.AddEditor(new Person(new UserId(staff.Id), staff.FullName), person, access, time.GetUtcNow().UtcDateTime);

        await db.SaveChangesAsync(ct);
        return true;
    }
}

internal sealed class RemoveArticleEditorHandler(KnowledgeDbContext db, ActingUser actingUser, TimeProvider time)
{
    // false when there is no such article.
    public async Task<bool> HandleAsync(RemoveArticleEditorCommand cmd, CancellationToken ct)
    {
        var id = new ArticleId(cmd.ArticleId);
        var article = await db.Articles.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (article is null) return false;

        var person = await actingUser.PersonAsync(ct);
        var access = await actingUser.AccessAsync(article.CreatedById, ct);
        article.RemoveEditor(new UserId(cmd.UserId), person, access, time.GetUtcNow().UtcDateTime);

        await db.SaveChangesAsync(ct);
        return true;
    }
}
