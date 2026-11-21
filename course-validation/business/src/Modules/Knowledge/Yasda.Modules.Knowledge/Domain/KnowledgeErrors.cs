using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Knowledge.Domain;

// KNOWLEDGE-0001 and up. Codes are never renumbered or reused.
internal static class KnowledgeErrors
{
    public static readonly ErrorCode NotAllowedInThisStatus = new("KNOWLEDGE-0001", "The article can't do that from its current status",
        "Reload to see where the article is. The message says which statuses allow this step.");
    public static readonly ErrorCode CannotPublishOwnChanges = new("KNOWLEDGE-0002", "You can't publish words you wrote yourself",
        "Ask another KbEditor or an Admin to review and publish it. The person who last changed the text never approves it.");
    public static readonly ErrorCode ArticleArchived = new("KNOWLEDGE-0003", "This article is archived",
        "An archived article can't change. Restore it first, then edit it.");
    public static readonly ErrorCode NotAllowedToEdit = new("KNOWLEDGE-0004", "You can't change this article",
        "Only its author, a team lead of the author's team, a KbEditor, an Admin or someone on its edit list may. Ask one of them to add you to the list.");
    public static readonly ErrorCode EditorNotStaff = new("KNOWLEDGE-0005", "That person can't be given edit access",
        "Choose an active agent or team lead from the list. KbEditors and admins can already edit every article.");
    public static readonly ErrorCode RejectReasonRequired = new("KNOWLEDGE-0006", "A reason is needed to send an article back",
        "Say in a few words what to change (up to 500 characters), so the author knows what to fix.");
    public static readonly ErrorCode TitleRequired = new("KNOWLEDGE-0007", "An article needs a title",
        "Write a short title that says what the article helps with, such as \"Reset your password\".");
    public static readonly ErrorCode TitleTooLong = new("KNOWLEDGE-0008", "The title is too long",
        "Keep the title to 200 characters.");
    public static readonly ErrorCode SummaryRequired = new("KNOWLEDGE-0009", "An article needs a summary",
        "Write one line under the title that tells a reader whether this is the article they need.");
    public static readonly ErrorCode SummaryTooLong = new("KNOWLEDGE-0010", "The summary is too long",
        "Keep the summary to 300 characters; the details belong in the body.");
    public static readonly ErrorCode BodyRequired = new("KNOWLEDGE-0011", "The article has no body",
        "Write the steps or the explanation before saving.");
    public static readonly ErrorCode BodyTooLong = new("KNOWLEDGE-0012", "The body is too long",
        "Keep the body to 20,000 characters. Split a long guide into two articles.");
}
