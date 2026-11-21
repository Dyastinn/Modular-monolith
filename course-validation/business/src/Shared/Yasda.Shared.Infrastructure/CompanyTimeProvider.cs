using Yasda.Shared.BuildingBlocks;

namespace Yasda.Shared.Infrastructure;

// The system clock, with the company's time zone as "local": at 7 a.m. in Manila it's still yesterday in UTC, and a day's
// count must use the local date. The zone is the calendar's, so it changes when an admin changes it. Tests use FakeTimeProvider instead.
public sealed class CompanyTimeProvider(CompanyZone zone) : TimeProvider
{
    public override TimeZoneInfo LocalTimeZone => zone.Current;
}
