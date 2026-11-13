using AwesomeAssertions;
using Erp.Shared.BuildingBlocks;
using Microsoft.Extensions.Time.Testing;

namespace Erp.Modules.Invoicing.UnitTests;

public class BusinessDateTests
{
    private static readonly TimeZoneInfo Manila = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");

    [Fact]
    public void Today_is_the_date_in_the_company_time_zone_not_in_UTC()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 12, 23, 30, 0, TimeSpan.Zero));
        time.SetLocalTimeZone(Manila);   // 07:30 on the 13th in Manila

        time.Today().Should().Be(new DateOnly(2026, 11, 13));
    }

    [Fact]
    public void Today_moves_when_the_clock_passes_local_midnight()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 12, 15, 59, 0, TimeSpan.Zero));
        time.SetLocalTimeZone(Manila);   // 23:59 on the 12th in Manila

        time.Today().Should().Be(new DateOnly(2026, 11, 12));
        time.Advance(TimeSpan.FromMinutes(1));
        time.Today().Should().Be(new DateOnly(2026, 11, 13));
    }
}
