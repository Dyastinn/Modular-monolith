using Yasda.Modules.Sla.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Sla.Features;

// One pass of the breach scheduler: find the clocks whose check time has come, and let each one warn or breach. Deadlines are rows,
// not timers, so a restart loses nothing: the next pass finds whatever is due.
//
// The rows are locked while they are looked at (FOR UPDATE SKIP LOCKED), so two scanners running at once (a second server, or a pass
// that overlaps the next) never take the same clock. Each warning and breach is saved in the same step as its event, and the clock
// remembers it was sent, so nothing is raised twice. This is a write path, so it uses EF Core, not Dapper.
internal sealed class SlaScanner(SlaDbContext db, TimeProvider time)
{
    public const int BatchSize = 100;

    // How many clocks were checked. A full batch means there may be more.
    public async Task<int> ScanAsync(CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var now = time.GetUtcNow().UtcDateTime;
        var due = await db.Clocks
            .FromSql($"SELECT *, xmin FROM sla.sla_clock WHERE next_check_utc <= {now} ORDER BY next_check_utc LIMIT {BatchSize} FOR UPDATE SKIP LOCKED")
            .ToListAsync(ct);

        foreach (var clock in due)
        {
            clock.Scan(now);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();
        return due.Count;
    }
}
