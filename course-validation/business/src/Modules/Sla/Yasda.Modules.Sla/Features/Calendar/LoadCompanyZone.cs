using Dapper;
using Yasda.Shared.BuildingBlocks;
using Microsoft.Extensions.Hosting;

namespace Yasda.Modules.Sla.Features;

// When the app starts, the saved calendar's time zone becomes the company's. It runs after the migrations (which the host does
// before it starts its services), so the calendar row is there.
internal sealed class LoadCompanyZone(IDbConnectionFactory connections, CompanyZone companyZone) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var conn = connections.Create();
        var zoneId = await conn.QuerySingleOrDefaultAsync<string>(new CommandDefinition("SELECT time_zone_id FROM sla.get_calendar()", cancellationToken: ct));
        if (zoneId is null) return;

        companyZone.Change(TimeZoneInfo.FindSystemTimeZoneById(zoneId));
    }

    public Task StopAsync(CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}
