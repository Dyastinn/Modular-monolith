namespace Erp.Shared.BuildingBlocks;

public static class TimeProviderExtensions
{
    // The calendar date where the business is (the provider's local time zone), for invoice and due dates.
    public static DateOnly Today(this TimeProvider time) => DateOnly.FromDateTime(time.GetLocalNow().DateTime);
}
