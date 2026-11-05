namespace Erp.Shared.BuildingBlocks;

// Inject instead of DateTime.UtcNow so time-based rules can be tested with a fixed clock.
public interface IClock
{
    DateTime UtcNow { get; }

    // The calendar date where the business is (Company:TimeZone), for invoice and due dates.
    DateOnly Today { get; }
}
