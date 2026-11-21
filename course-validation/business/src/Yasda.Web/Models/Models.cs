using Yasda.Shared.BuildingBlocks;

namespace Yasda.Web.Models;

// The API's JSON shapes, as the browser sees them. The server's own types are internal to each module, so the
// browser app keeps its own copies: the HTTP contract is the only thing the two share.

public sealed record User(long Id, string Email, string FullName, string Role, bool IsDisabled, bool MustChangePassword,
    DateTime? LockedOutUntil, DateTime? LastSignIn)
{
    public string Status => IsDisabled ? "Disabled"
        : LockedOutUntil is not null ? "Locked out"
        : MustChangePassword ? "Temporary password"
        : "Active";
}

public sealed record SignInAttempt(long Id, string Email, long? UserId, string Outcome, string? IpAddress, DateTime OccurredUtc);

public sealed record TicketSummary(long Id, string Subject, string Status, string Priority, long RequesterId, string RequesterName,
    long? AssigneeId, string? AssigneeName, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? NextDueUtc, DateTime? AtRiskFromUtc,
    string? TeamName, string? CategoryName);

public sealed record TicketMessage(long Id, long AuthorId, string AuthorName, string Body, bool IsInternal, DateTime CreatedUtc);

public sealed record TicketDetail(long Id, string Subject, string Status, string Priority, long RequesterId, string RequesterName,
    long? AssigneeId, string? AssigneeName, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? FirstResponseUtc,
    DateTime? ResolvedUtc, DateTime? ClosedUtc, string? CancelReason, long? CategoryId, string? CategoryName, long? TeamId, string? TeamName,
    TicketSlaFacts? Sla, List<TicketMessage> Messages)
{
    public bool IsBeingWorkedOn => Status is "New" or "Open" or "Pending";
    public bool IsFinished => Status is "Closed" or "Cancelled";
}

// The deadlines Sla worked out for a ticket. Only staff ever receive them.
public sealed record TicketSlaFacts(DateTime ResponseDueUtc, bool? ResponseMet, DateTime ResolveDueUtc, bool? ResolutionMet,
    DateTime? NextDueUtc, DateTime? AtRiskFromUtc);

public sealed record SlaPolicy(string Priority, int RespondMinutes, int ResolveMinutes, DateTime UpdatedUtc, long UpdatedById, string UpdatedByName);

public sealed record TeamSummary(long Id, string Name, bool IsActive, string NotifyMode, long MemberCount);

public sealed record TeamMember(long UserId, string FullName, string Role, bool Notify, bool IsDisabled);

public sealed record TeamDetail(long Id, string Name, bool IsActive, string NotifyMode, List<TeamMember> Members);

public sealed record Category(long Id, string Name, long? TeamId, string? TeamName, bool IsActive);

public sealed record StaffMember(long Id, string FullName, string Role);

// The knowledge base's JSON shapes. An article has a working copy (what editors change) and published versions (what readers see).
public sealed record ArticleSummary(long Id, string Title, string Summary, string Status, string Visibility, int CurrentVersion,
    long CreatedById, string CreatedByName, string LastEditedByName, DateTime UpdatedUtc);

public sealed record ArticleVersionInfo(int Number, string Title, string Visibility, long PublishedById, string PublishedByName, DateTime PublishedUtc);

public sealed record ArticleEditorInfo(long UserId, string UserName, string AddedByName, DateTime AddedUtc);

public sealed record ArticleHistoryLine(string Action, long ActorId, string ActorName, DateTime AtUtc, string? Note, int? VersionNumber);

// CanEdit and CanManageEditors say what this person may do, so a page shows only the controls that will work.
public sealed record ArticleDetail(long Id, string Title, string Summary, string Body, string Status, string Visibility, int CurrentVersion,
    long CreatedById, string CreatedByName, long LastEditedById, string LastEditedByName, DateTime CreatedUtc, DateTime UpdatedUtc,
    string? RejectionReason, bool CanEdit, bool CanManageEditors,
    List<ArticleVersionInfo> Versions, List<ArticleEditorInfo> Editors, List<ArticleHistoryLine> History);

public sealed record PublishedSummary(long Id, string Title, string Summary, string Visibility, DateTime PublishedUtc);

public sealed record PublishedArticle(long Id, string Title, string Summary, string Body, string Visibility, int VersionNumber, DateTime PublishedUtc);

public sealed record PopularArticle(long Id, string Title, string Summary, long Views);

public static class KnowledgeChoices
{
    public static readonly string[] Statuses = ["Draft", "InReview", "Published", "Archived"];
    public static readonly string[] Visibilities = ["Public", "Internal"];

    // Who writes articles, and who decides on them. The API decides; the screens only hide what someone can't do anyway.
    public static bool IsWriter(string? role) => role is Roles.Agent or Roles.TeamLead or Roles.KbEditor or Roles.Admin;
    public static bool IsEditor(string? role) => role is Roles.KbEditor or Roles.Admin;

    public static string StatusText(string status)
    {
        if (status == "InReview") return "In review";
        return status;
    }

    // One line of the history, in words.
    public static string ActionText(string action)
    {
        switch (action)
        {
            case "Created": return "Started the draft";
            case "Edited": return "Changed the words";
            case "Submitted": return "Asked for a review";
            case "Rejected": return "Sent it back";
            case "Published": return "Published";
            case "Archived": return "Archived";
            case "Restored": return "Restored it as a draft";
            case "EditorAdded": return "Added to the edit list";
            case "EditorRemoved": return "Removed from the edit list";
            default: return action;
        }
    }
}

// The choices the API accepts, in the order people think of them.
public static class TicketChoices
{
    public static readonly string[] Priorities = ["Low", "Normal", "High", "Critical"];
    public static readonly string[] Statuses = ["New", "Open", "Pending", "Resolved", "Closed", "Cancelled"];

    // Who works on tickets. The API decides; the screens only hide what someone can't do anyway.
    public static bool IsStaff(string? role) => role is Roles.Agent or Roles.TeamLead or Roles.Admin;
    public static readonly string[] NotifyModes = ["LeadsOnly", "LeadsAndSelected", "Everyone"];

    // How a team's notify mode reads on a screen.
    public static string NotifyText(string mode)
    {
        switch (mode)
        {
            case "LeadsAndSelected": return "Team leads and selected members";
            case "Everyone": return "Everyone in the team";
            default: return "Team leads only";
        }
    }

    public static bool IsLead(string? role) => role is Roles.TeamLead or Roles.Admin;
}
