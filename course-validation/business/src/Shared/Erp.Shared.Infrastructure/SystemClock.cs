using Erp.Shared.BuildingBlocks;
using Microsoft.Extensions.Configuration;

namespace Erp.Shared.Infrastructure;

public sealed class SystemClock(IConfiguration config) : IClock
{
    // At 7 a.m. in Manila it's still yesterday in UTC; an invoice must carry the local date.
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(config["Company:TimeZone"] ?? "Asia/Manila");

    public DateTime UtcNow => DateTime.UtcNow;
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _zone));
}
