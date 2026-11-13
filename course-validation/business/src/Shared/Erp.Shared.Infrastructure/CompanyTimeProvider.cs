namespace Erp.Shared.Infrastructure;

// The system clock, with the company's time zone as "local": at 7 a.m. in Manila it's still yesterday in UTC, and an
// invoice must carry the local date. Tests use FakeTimeProvider instead.
public sealed class CompanyTimeProvider(TimeZoneInfo zone) : TimeProvider
{
    public override TimeZoneInfo LocalTimeZone => zone;
}
