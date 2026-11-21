using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Sla.Domain;

// SLA-0001 and up. Codes are never renumbered or reused.
internal static class SlaErrors
{
    public static readonly ErrorCode RespondTargetNotPositive = new("SLA-0001", "The response target must be more than zero",
        "Enter how long a team has to answer a new ticket, in minutes, as a number above zero.");
    public static readonly ErrorCode ResolveBeforeRespond = new("SLA-0002", "The resolve target can't be shorter than the response target",
        "A ticket can't be due to be resolved before it is due to be answered. Raise the resolve target, or lower the response target.");
    public static readonly ErrorCode CalendarHasNoOpenDay = new("SLA-0003", "The calendar needs at least one open day",
        "Give at least one weekday opening and closing times. Without one, no deadline could ever be reached.");
    public static readonly ErrorCode ClosingBeforeOpening = new("SLA-0004", "A day must close after it opens",
        "Set the closing time later than the opening time on that day, or mark the day closed.");
}
