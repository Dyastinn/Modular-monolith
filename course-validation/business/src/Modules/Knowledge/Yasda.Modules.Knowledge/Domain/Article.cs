using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Knowledge.Domain;

// A help article, from first draft to the versions readers see. Every rule about who may change it and what may happen next
// lives here.
//
// An article has a working copy (Title, Summary, Body, Visibility: what editors change) and a list of published versions (what
// readers see). Editing never changes a version; only publishing adds one. So while a published article is being revised,
// readers keep the last published text.
internal sealed class Article : AggregateRoot<ArticleId>
{
    public const int TitleMaxLength = 200;
    public const int SummaryMaxLength = 300;
    public const int BodyMaxLength = 20_000;
    public const int ReasonMaxLength = 500;

    private readonly List<ArticleVersion> _versions = [];
    private readonly List<ArticleEditor> _editors = [];
    private readonly List<ArticleHistoryEntry> _history = [];

    private Article() { }

    public string Title { get; private set; } = "";
    public string Summary { get; private set; } = "";
    public string Body { get; private set; } = "";
    public ArticleVisibility Visibility { get; private set; }
    public ArticleStatus Status { get; private set; }
    public UserId CreatedById { get; private set; }
    public string CreatedByName { get; private set; } = "";
    // Whoever last changed the text: the one person who can't publish it (unless they are an Admin).
    public UserId LastEditedById { get; private set; }
    public string LastEditedByName { get; private set; } = "";
    public DateTime CreatedUtc { get; private set; }
    // Touched by every change, including a new version, history row or edit-list entry, so the row's xmin changes and a
    // second person working from an older copy is refused.
    public DateTime UpdatedUtc { get; private set; }
    // The number of the newest published version; 0 while the article has never been published.
    public int CurrentVersion { get; private set; }
    // Why the last review sent it back. Cleared when it is submitted again.
    public string? RejectionReason { get; private set; }

    public IReadOnlyList<ArticleVersion> Versions => _versions;
    public IReadOnlyList<ArticleEditor> Editors => _editors;
    public IReadOnlyList<ArticleHistoryEntry> History => _history;

    public static Article Create(string title, string summary, string body, ArticleVisibility visibility, Person author, DateTime nowUtc)
    {
        var article = new Article
        {
            Title = CheckedTitle(title),
            Summary = CheckedSummary(summary),
            Body = CheckedBody(body),
            Visibility = visibility,
            Status = ArticleStatus.Draft,
            CreatedById = author.Id,
            CreatedByName = author.Name,
            LastEditedById = author.Id,
            LastEditedByName = author.Name,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
        };
        article.Record(ArticleAction.Created, author, nowUtc);
        return article;
    }

    // Changes the working copy. A published article goes back to Draft (its readers keep the version they have); one in review
    // must first be sent back, so nobody changes words a reviewer is reading.
    public void Edit(string title, string summary, string body, ArticleVisibility visibility, Person actor, ArticleAccess access, DateTime nowUtc)
    {
        EnsureNotArchived();
        EnsureMayEdit(actor, access);
        EnsureIn("edit", ArticleStatus.Draft, ArticleStatus.Published);

        Title = CheckedTitle(title);
        Summary = CheckedSummary(summary);
        Body = CheckedBody(body);
        Visibility = visibility;
        LastEditedById = actor.Id;
        LastEditedByName = actor.Name;
        Status = ArticleStatus.Draft;
        UpdatedUtc = nowUtc;
        Record(ArticleAction.Edited, actor, nowUtc);
    }

    // The author is done and asks for a review.
    public void Submit(Person actor, ArticleAccess access, DateTime nowUtc)
    {
        EnsureNotArchived();
        EnsureMayEdit(actor, access);
        EnsureIn("submit", ArticleStatus.Draft);

        Status = ArticleStatus.InReview;
        RejectionReason = null;
        UpdatedUtc = nowUtc;
        Record(ArticleAction.Submitted, actor, nowUtc);
        Raise(new ArticleSubmittedEvent(Guid.NewGuid(), Id, Title, actor.Id));
    }

    // A reviewer sends it back to the author, and says why.
    public void Reject(string reason, Person actor, DateTime nowUtc)
    {
        EnsureNotArchived();
        EnsureIn("send back", ArticleStatus.InReview);
        var text = CheckedReason(reason);

        Status = ArticleStatus.Draft;
        RejectionReason = text;
        UpdatedUtc = nowUtc;
        Record(ArticleAction.Rejected, actor, nowUtc, note: text);
        Raise(new ArticleRejectedEvent(Guid.NewGuid(), Id, Title, CreatedById, actor.Id, text));
    }

    // A reviewer approves it: the working copy becomes the next numbered version, which readers see from now on. The person
    // who last changed the text can't approve it, unless they are an Admin (so an installation with one person can still publish).
    public void Publish(Person actor, bool actorIsAdmin, DateTime nowUtc)
    {
        EnsureNotArchived();
        EnsureIn("publish", ArticleStatus.InReview);
        if (actor.Id == LastEditedById && !actorIsAdmin)
            throw new DomainException(KnowledgeErrors.CannotPublishOwnChanges,
                $"{actor.Name} last changed this article, so someone else has to publish it.");

        CurrentVersion = CurrentVersion + 1;
        _versions.Add(new ArticleVersion(CurrentVersion, Title, Summary, Body, Visibility, actor, nowUtc));
        Status = ArticleStatus.Published;
        RejectionReason = null;
        UpdatedUtc = nowUtc;
        Record(ArticleAction.Published, actor, nowUtc, versionNumber: CurrentVersion);
        Raise(new ArticlePublishedEvent(Guid.NewGuid(), Id, Title, CreatedById, actor.Id, CurrentVersion));
    }

    // Takes the article away from readers, whatever its status. Its versions stay.
    public void Archive(Person actor, DateTime nowUtc)
    {
        EnsureNotArchived();

        Status = ArticleStatus.Archived;
        UpdatedUtc = nowUtc;
        Record(ArticleAction.Archived, actor, nowUtc);
    }

    // Brings it back as a draft. If it was ever published, readers see its newest version again at once.
    public void Restore(Person actor, DateTime nowUtc)
    {
        EnsureIn("restore", ArticleStatus.Archived);

        Status = ArticleStatus.Draft;
        UpdatedUtc = nowUtc;
        Record(ArticleAction.Restored, actor, nowUtc);
    }

    // Lets one more person edit this article. Adding the author, or someone already on the list, changes nothing.
    public void AddEditor(Person person, Person actor, ArticleAccess access, DateTime nowUtc)
    {
        EnsureMayManageEditors(actor, access);
        if (person.Id == CreatedById) return;
        if (FindEditor(person.Id) is not null) return;

        _editors.Add(new ArticleEditor(person, actor, nowUtc));
        UpdatedUtc = nowUtc;
        Record(ArticleAction.EditorAdded, actor, nowUtc, note: person.Name);
    }

    // Takes someone off the edit list. Removing someone who isn't on it changes nothing.
    public void RemoveEditor(UserId userId, Person actor, ArticleAccess access, DateTime nowUtc)
    {
        EnsureMayManageEditors(actor, access);
        var editor = FindEditor(userId);
        if (editor is null) return;

        _editors.Remove(editor);
        UpdatedUtc = nowUtc;
        Record(ArticleAction.EditorRemoved, actor, nowUtc, note: editor.UserName);
    }

    // May this person change the article's words? The author, someone on the edit list, a KbEditor or Admin, or a team lead of the
    // author's team. Static, so a screen's query can ask the same question without loading the article.
    public static bool MayEdit(UserId author, IEnumerable<UserId> editList, UserId person, ArticleAccess access)
    {
        if (person == author) return true;
        if (editList.Contains(person)) return true;
        if (access.IsEditor) return true;
        if (access.LeadsAuthorsTeam) return true;
        return false;
    }

    // The edit list is managed by the author and the people above them, not by someone who was only lent edit access.
    public static bool MayManageEditors(UserId author, UserId person, ArticleAccess access)
    {
        if (person == author) return true;
        if (access.IsEditor) return true;
        if (access.LeadsAuthorsTeam) return true;
        return false;
    }

    private ArticleEditor? FindEditor(UserId userId)
    {
        foreach (var editor in _editors)
        {
            if (editor.UserId == userId) return editor;
        }
        return null;
    }

    private void EnsureMayEdit(Person actor, ArticleAccess access)
    {
        var onTheList = _editors.Select(e => e.UserId);
        if (MayEdit(CreatedById, onTheList, actor.Id, access)) return;
        throw new DomainException(KnowledgeErrors.NotAllowedToEdit);
    }

    private void EnsureMayManageEditors(Person actor, ArticleAccess access)
    {
        if (MayManageEditors(CreatedById, actor.Id, access)) return;
        throw new DomainException(KnowledgeErrors.NotAllowedToEdit);
    }

    private void EnsureNotArchived()
    {
        if (Status != ArticleStatus.Archived) return;
        throw new DomainException(KnowledgeErrors.ArticleArchived, $"Article #{Id.Value} is archived.");
    }

    private void EnsureIn(string action, params ArticleStatus[] allowed)
    {
        if (allowed.Contains(Status)) return;

        var statuses = string.Join(" or ", allowed.Select(s => s.ToString()));
        throw new DomainException(KnowledgeErrors.NotAllowedInThisStatus,
            $"Can't {action} article #{Id.Value}: it is {Status}, and this needs {statuses}.");
    }

    private void Record(ArticleAction action, Person actor, DateTime atUtc, string? note = null, int? versionNumber = null)
    {
        _history.Add(new ArticleHistoryEntry(action, actor, atUtc, note, versionNumber));
    }

    private static string CheckedTitle(string title)
    {
        var text = title?.Trim() ?? "";
        if (text.Length == 0) throw new DomainException(KnowledgeErrors.TitleRequired);
        if (text.Length > TitleMaxLength) throw new DomainException(KnowledgeErrors.TitleTooLong);
        return text;
    }

    private static string CheckedSummary(string summary)
    {
        var text = summary?.Trim() ?? "";
        if (text.Length == 0) throw new DomainException(KnowledgeErrors.SummaryRequired);
        if (text.Length > SummaryMaxLength) throw new DomainException(KnowledgeErrors.SummaryTooLong);
        return text;
    }

    private static string CheckedBody(string body)
    {
        var text = body?.Trim() ?? "";
        if (text.Length == 0) throw new DomainException(KnowledgeErrors.BodyRequired);
        if (text.Length > BodyMaxLength) throw new DomainException(KnowledgeErrors.BodyTooLong);
        return text;
    }

    private static string CheckedReason(string reason)
    {
        var text = reason?.Trim() ?? "";
        if (text.Length == 0 || text.Length > ReasonMaxLength) throw new DomainException(KnowledgeErrors.RejectReasonRequired);
        return text;
    }
}
