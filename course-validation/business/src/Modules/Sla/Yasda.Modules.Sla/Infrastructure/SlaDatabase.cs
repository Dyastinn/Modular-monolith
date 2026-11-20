using System.Data.Common;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Sla.Infrastructure;

internal sealed class SlaDatabase : IModuleDatabase
{
    public string Module => "Sla";
    public string Schema => SlaDbContext.Schema;

    public async Task MigrateAsync(DbConnection ownerConnection, CancellationToken ct)
    {
        await using var db = Open(ownerConnection);
        await db.Database.MigrateAsync(ct);
    }

    // The policies are seeded by a migration (every installation needs them), so there is no sample data to add.
    public Task SeedDevelopmentDataAsync(DbConnection ownerConnection, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    private static SlaDbContext Open(DbConnection connection)
    {
        var options = new DbContextOptionsBuilder<SlaDbContext>();
        SlaDbContext.Configure(options, connection);
        return new SlaDbContext(options.Options);
    }
}
