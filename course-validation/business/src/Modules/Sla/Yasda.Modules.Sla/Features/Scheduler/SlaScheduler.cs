using Yasda.Shared.BuildingBlocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Yasda.Modules.Sla.Features;

// Wakes up every Sla:SchedulerSeconds (30 unless the installation says otherwise) and runs the scanner until it has nothing left to do.
// A pass that fails is logged and tried again at the next wake-up: one bad moment must not end the scheduler.
internal sealed class SlaScheduler(IServiceScopeFactory scopes, TimeProvider time, IConfiguration config, ILogger<SlaScheduler> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(config.GetValue("Sla:SchedulerSeconds", 30));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await ScanUntilQuietAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "The SLA scheduler could not finish a pass; it will try again in {Seconds} seconds", interval.TotalSeconds);
            }
        }
    }

    private async Task ScanUntilQuietAsync(CancellationToken ct)
    {
        while (true)
        {
            // A new scope for each pass: a DbContext is meant to live for one unit of work.
            await using var scope = scopes.CreateAsyncScope();
            var scanner = scope.ServiceProvider.GetRequiredService<SlaScanner>();
            var checkedClocks = await scanner.ScanAsync(ct);
            if (checkedClocks < SlaScanner.BatchSize) return;
        }
    }
}
