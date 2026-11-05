using System.Data.Common;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Purchasing.Infrastructure;

internal sealed class PurchasingDatabase : IModuleDatabase
{
    public string Module => "Purchasing";
    public string Schema => PurchasingDbContext.Schema;

    public async Task MigrateAsync(DbConnection ownerConnection, CancellationToken ct)
    {
        var options = new DbContextOptionsBuilder<PurchasingDbContext>();
        PurchasingDbContext.Configure(options, ownerConnection);
        await using var db = new PurchasingDbContext(options.Options);
        await db.Database.MigrateAsync(ct);
    }

    public Task SeedDevelopmentDataAsync(DbConnection ownerConnection, CancellationToken ct) =>
        EmbeddedSql.ExecuteAsync(ownerConnection, EmbeddedSql.Read(typeof(PurchasingDatabase).Assembly, "DevSeed.sql"), ct);
}
