namespace Yasda.Shared.BuildingBlocks;

// The organization's time zone: what "today" means, and where the working day is counted. It lives on the calendar screen, and
// this holder keeps the current value in memory: loaded when the app starts, replaced when an admin saves the calendar.
// One instance of the app is assumed (the target is Compose on one host); several would each need to hear about a change.
public sealed class CompanyZone
{
    public const string DefaultZoneId = "Asia/Manila";

    public TimeZoneInfo Current { get; private set; } = TimeZoneInfo.FindSystemTimeZoneById(DefaultZoneId);

    public void Change(TimeZoneInfo zone)
    {
        Current = zone;
    }
}
