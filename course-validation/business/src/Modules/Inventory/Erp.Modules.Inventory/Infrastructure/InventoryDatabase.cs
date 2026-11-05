using System.Data.Common;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Inventory.Infrastructure;

internal sealed class InventoryDatabase : IModuleDatabase
{
    public string Module => "Inventory";
    public string Schema => InventoryDbContext.Schema;

    public async Task MigrateAsync(DbConnection ownerConnection, CancellationToken ct)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>();
        InventoryDbContext.Configure(options, ownerConnection);
        await using var db = new InventoryDbContext(options.Options);
        await db.Database.MigrateAsync(ct);
    }

    public Task SeedDevelopmentDataAsync(DbConnection ownerConnection, CancellationToken ct) =>
        EmbeddedSql.ExecuteAsync(ownerConnection, EmbeddedSql.Read(typeof(InventoryDatabase).Assembly, "DevSeed.sql"), ct);
}
