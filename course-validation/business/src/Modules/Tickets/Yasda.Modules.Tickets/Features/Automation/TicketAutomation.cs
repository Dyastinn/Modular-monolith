using Yasda.Modules.Tickets.Domain;
using Yasda.Modules.Tickets.Infrastructure;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Yasda.Modules.Tickets.Features;

internal sealed record AutomationResult(int Closed, int Reminded, int Cancelled);

// One pass of the automatic rules (decision E4): each rule an Admin has switched on is applied to the tickets it covers. Rules with no
// number are off and do nothing. Each ticket is changed and saved by itself, so one that someone else is editing at that moment is skipped
// and found again at the next pass, and one bad ticket never holds up the rest.
internal sealed class TicketAutomationRunner(TicketsDbContext db, TimeProvider time)
{
    private const int BatchSize = 100;

    public async Task<AutomationResult> RunAsync(CancellationToken ct)
    {
        var settings = await db.AutomationSettings.AsNoTracking().SingleAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;

        var cancelled = 0;
        if (settings.ClosePendingAfterDays is not null) cancelled = await CancelSilentTicketsAsync(settings.ClosePendingAfterDays.Value, now, ct);

        var reminded = 0;
        if (settings.RemindPendingAfterDays is not null) reminded = await RemindSilentRequestersAsync(settings.RemindPendingAfterDays.Value, now, ct);

        var closed = 0;
        if (settings.CloseResolvedAfterDays is not null) closed = await CloseResolvedTicketsAsync(settings.CloseResolvedAfterDays.Value, now, ct);

        return new AutomationResult(closed, reminded, cancelled);
    }

    private async Task<int> CloseResolvedTicketsAsync(int days, DateTime now, CancellationToken ct)
    {
        var cutoff = now.AddDays(-days);
        var tickets = await db.Tickets.Where(t => t.Status == TicketStatus.Resolved && t.ResolvedUtc <= cutoff).OrderBy(t => t.Id).Take(BatchSize).ToListAsync(ct);

        var done = 0;
        foreach (var ticket in tickets)
        {
            ticket.Close(now);
            if (await TrySaveAsync(ct)) done++;
        }
        return done;
    }

    private async Task<int> RemindSilentRequestersAsync(int days, DateTime now, CancellationToken ct)
    {
        var cutoff = now.AddDays(-days);
        var tickets = await db.Tickets
            .Where(t => t.Status == TicketStatus.Pending && t.PendingSinceUtc <= cutoff && t.ReminderSentUtc == null)
            .OrderBy(t => t.Id).Take(BatchSize).ToListAsync(ct);

        var done = 0;
        foreach (var ticket in tickets)
        {
            ticket.RemindRequester(now);
            if (await TrySaveAsync(ct)) done++;
        }
        return done;
    }

    // A ticket the requester never answered is CANCELLED, not closed or resolved: nobody fixed it, so it must not count as a resolution. The reason
    // on the ticket says it was automatic.
    private async Task<int> CancelSilentTicketsAsync(int days, DateTime now, CancellationToken ct)
    {
        var cutoff = now.AddDays(-days);
        var tickets = await db.Tickets.Where(t => t.Status == TicketStatus.Pending && t.PendingSinceUtc <= cutoff).OrderBy(t => t.Id).Take(BatchSize).ToListAsync(ct);

        var done = 0;
        foreach (var ticket in tickets)
        {
            ticket.CancelForSilence(days, now);
            if (await TrySaveAsync(ct)) done++;
        }
        return done;
    }

    // false when someone else changed the ticket first: it is left as they made it, and the next pass looks at it again.
    private async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }
}

// Wakes up every Tickets:AutomationMinutes (60 unless the installation says otherwise) and runs the rules. A pass that fails is logged and
// tried again at the next wake-up.
internal sealed class TicketAutomationService(IServiceScopeFactory scopes, TimeProvider time, IConfiguration config, ILogger<TicketAutomationService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(config.GetValue("Tickets:AutomationMinutes", 60));

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
                await using var scope = scopes.CreateAsyncScope();
                var runner = scope.ServiceProvider.GetRequiredService<TicketAutomationRunner>();
                var result = await runner.RunAsync(stoppingToken);
                if (result.Closed + result.Reminded + result.Cancelled > 0)
                    log.LogInformation("Automatic rules: {Closed} closed, {Reminded} reminded, {Cancelled} cancelled", result.Closed, result.Reminded, result.Cancelled);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "The automatic rules could not finish a pass; they will run again in {Minutes} minutes", interval.TotalMinutes);
            }
        }
    }
}
