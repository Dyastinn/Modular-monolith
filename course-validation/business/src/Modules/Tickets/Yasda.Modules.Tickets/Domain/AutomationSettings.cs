using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Domain;

// The automatic rules an Admin can switch on (decision E4). Nothing is automatic until it is switched on: a rule with no number is off.
//   - Close a ticket that has been Resolved for this many days.
//   - Remind the requester of a ticket that has waited on them this many days with no answer.
//   - Cancel a ticket that has waited on the requester this many days (they never answered).
internal sealed class AutomationSettings : AggregateRoot<AutomationSettingsId>
{
    // What the screen suggests when a rule is switched on: the reopen window, a week of silence, then two weeks.
    public const int SuggestedCloseResolvedDays = 7;
    public const int SuggestedRemindPendingDays = 7;
    public const int SuggestedClosePendingDays = 14;

    private AutomationSettings() { }

    public int? CloseResolvedAfterDays { get; private set; }
    public int? RemindPendingAfterDays { get; private set; }
    public int? ClosePendingAfterDays { get; private set; }

    public DateTime UpdatedUtc { get; private set; }
    public UserId UpdatedById { get; private set; }
    public string UpdatedByName { get; private set; } = "";

    // The real row is seeded by the migration, everything off; Create is how the domain makes one (and how the tests do).
    public static AutomationSettings Create(UserId by, string byName, DateTime nowUtc)
    {
        var settings = new AutomationSettings { Id = new AutomationSettingsId(1) };
        settings.UpdatedUtc = nowUtc;
        settings.UpdatedById = by;
        settings.UpdatedByName = byName;
        return settings;
    }

    // The numbers are days, and the range of a number is checked where the request comes in. The rule here is the one that needs the
    // other numbers: asking again and cancelling the ticket in the same breath would make the reminder pointless.
    public void Change(int? closeResolvedAfterDays, int? remindPendingAfterDays, int? closePendingAfterDays, UserId by, string byName, DateTime nowUtc)
    {
        var bothPendingRulesOn = remindPendingAfterDays is not null && closePendingAfterDays is not null;
        if (bothPendingRulesOn && closePendingAfterDays <= remindPendingAfterDays)
            throw new DomainException(TicketsErrors.CloseBeforeReminder);

        CloseResolvedAfterDays = closeResolvedAfterDays;
        RemindPendingAfterDays = remindPendingAfterDays;
        ClosePendingAfterDays = closePendingAfterDays;
        UpdatedUtc = nowUtc;
        UpdatedById = by;
        UpdatedByName = byName;
    }
}
