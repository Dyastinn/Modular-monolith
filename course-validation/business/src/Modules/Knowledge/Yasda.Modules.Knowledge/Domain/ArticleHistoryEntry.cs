namespace Yasda.Modules.Knowledge.Domain;

// One line of an article's audit trail: who did what, and when. Only ever added, never changed or removed.
internal sealed class ArticleHistoryEntry
{
    private ArticleHistoryEntry() { }

    internal ArticleHistoryEntry(ArticleAction action, Person actor, DateTime atUtc, string? note, int? versionNumber)
    {
        Action = action;
        ActorId = actor.Id;
        ActorName = actor.Name;
        AtUtc = atUtc;
        Note = note;
        VersionNumber = versionNumber;
    }

    public long Id { get; private set; }
    public ArticleAction Action { get; private set; }
    public UserId ActorId { get; private set; }
    public string ActorName { get; private set; } = "";
    public DateTime AtUtc { get; private set; }
    // The reason for a rejection, or the person added to or removed from the edit list.
    public string? Note { get; private set; }
    public int? VersionNumber { get; private set; }
}
