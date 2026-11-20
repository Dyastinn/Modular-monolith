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
    long? AssigneeId, string? AssigneeName, DateTime CreatedUtc, DateTime UpdatedUtc);

public sealed record TicketMessage(long Id, long AuthorId, string AuthorName, string Body, bool IsInternal, DateTime CreatedUtc);

public sealed record TicketDetail(long Id, string Subject, string Status, string Priority, long RequesterId, string RequesterName,
    long? AssigneeId, string? AssigneeName, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? FirstResponseUtc,
    DateTime? ResolvedUtc, DateTime? ClosedUtc, string? CancelReason, List<TicketMessage> Messages)
{
    public bool IsBeingWorkedOn => Status is "New" or "Open" or "Pending";
    public bool IsFinished => Status is "Closed" or "Cancelled";
}

public sealed record StaffMember(long Id, string FullName, string Role);

// The choices the API accepts, in the order people think of them.
public static class TicketChoices
{
    public static readonly string[] Priorities = ["Low", "Normal", "High", "Critical"];
    public static readonly string[] Statuses = ["New", "Open", "Pending", "Resolved", "Closed", "Cancelled"];

    // Who works on tickets. The API decides; the screens only hide what someone can't do anyway.
    public static bool IsStaff(string? role) => role is Roles.Agent or Roles.TeamLead or Roles.Admin;
    public static bool IsLead(string? role) => role is Roles.TeamLead or Roles.Admin;
}
