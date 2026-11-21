using Dapper;
using Yasda.Modules.Knowledge.Domain;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Knowledge.Features;

internal sealed record GetArticleQuery(long ArticleId);

// CanEdit and CanManageEditors are what this person may do, so the page shows only the controls that will work. The article
// still decides when they use them.
internal sealed record ArticleDetailDto(long Id, string Title, string Summary, string Body, string Status, string Visibility, int CurrentVersion,
    long CreatedById, string CreatedByName, long LastEditedById, string LastEditedByName, DateTime CreatedUtc, DateTime UpdatedUtc,
    string? RejectionReason, bool CanEdit, bool CanManageEditors,
    IReadOnlyList<ArticleVersionDto> Versions, IReadOnlyList<ArticleEditorDto> Editors, IReadOnlyList<ArticleHistoryDto> History);

internal sealed record ArticleVersionDto(int Number, string Title, string Visibility, long PublishedById, string PublishedByName, DateTime PublishedUtc);

internal sealed record ArticleEditorDto(long UserId, string UserName, string AddedByName, DateTime AddedUtc);

internal sealed record ArticleHistoryDto(string Action, long ActorId, string ActorName, DateTime AtUtc, string? Note, int? VersionNumber);

// One row of knowledge.get_article, exactly as the SQL function returns it.
internal sealed record ArticleRow(long Id, string Title, string Summary, string Body, string Status, string Visibility, int CurrentVersion,
    long CreatedById, string CreatedByName, long LastEditedById, string LastEditedByName, DateTime CreatedUtc, DateTime UpdatedUtc, string? RejectionReason);

internal sealed class GetArticleHandler(IDbConnectionFactory connections, ActingUser actingUser)
{
    public async Task<ArticleDetailDto?> HandleAsync(GetArticleQuery q, CancellationToken ct)
    {
        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM knowledge.get_article(@ArticleId);
            SELECT * FROM knowledge.get_article_versions(@ArticleId);
            SELECT * FROM knowledge.get_article_editors(@ArticleId);
            SELECT * FROM knowledge.get_article_history(@ArticleId);
            """, new { q.ArticleId }, cancellationToken: ct));

        var row = await multi.ReadSingleOrDefaultAsync<ArticleRow>();
        if (row is null) return null;
        var versions = (await multi.ReadAsync<ArticleVersionDto>()).ToList();
        var editors = (await multi.ReadAsync<ArticleEditorDto>()).ToList();
        var history = (await multi.ReadAsync<ArticleHistoryDto>()).ToList();

        // The same two rules the article applies, asked here only to decide which controls to show.
        var author = new UserId(row.CreatedById);
        var access = await actingUser.AccessAsync(author, ct);
        var onTheList = new List<UserId>();
        foreach (var editor in editors)
            onTheList.Add(new UserId(editor.UserId));
        var canEdit = Article.MayEdit(author, onTheList, actingUser.Id, access);
        var canManageEditors = Article.MayManageEditors(author, actingUser.Id, access);

        return new ArticleDetailDto(row.Id, row.Title, row.Summary, row.Body, row.Status, row.Visibility, row.CurrentVersion,
            row.CreatedById, row.CreatedByName, row.LastEditedById, row.LastEditedByName, row.CreatedUtc, row.UpdatedUtc,
            row.RejectionReason, canEdit, canManageEditors, versions, editors, history);
    }
}
