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
    public static readonly ErrorCode UnknownTimeZone = new("SLA-0005", "That time zone isn't known",
        "Use a time zone name such as Asia/Manila. The list on the calendar screen holds the names this server knows.");
    public static readonly ErrorCode DayListedTwice = new("SLA-0006", "A weekday is listed twice",
        "Send each weekday once, with its opening and closing times, or with none to close it.");
    public static readonly ErrorCode HolidayNameRequired = new("SLA-0007", "A holiday needs a name",
        "Give the holiday a short name, up to 100 characters, such as Christmas Day.");
    public static readonly ErrorCode HolidayAlreadyListed = new("SLA-0008", "That date is already a holiday",
        "Each date can be a holiday once. Remove the old one first if you want to rename it.");
    public static readonly ErrorCode HolidayLineInvalid = new("SLA-0009", "A line of the holiday file can't be read",
        "Write each holiday on its own line as a date and a name, like 2026-12-25,Christmas Day. Fix the line named in the message and import again.");
    public static readonly ErrorCode UnknownHolidayPreset = new("SLA-0010", "That holiday list isn't known",
        "Pick one of the lists the calendar screen offers. Anything else can be imported from a file.");
}
