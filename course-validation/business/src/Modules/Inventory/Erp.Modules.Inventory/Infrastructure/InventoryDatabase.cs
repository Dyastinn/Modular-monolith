using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Inventory.Infrastructure;

internal sealed class InventoryDatabase(InventoryDbContext db) : IModuleDatabase
{
    public string Module => "Inventory";

    public Task MigrateAsync(CancellationToken ct) => db.Database.MigrateAsync(ct);

    public Task SeedDevelopmentDataAsync(CancellationToken ct) =>
        EmbeddedSql.ExecuteAsync(
            db.Database.GetDbConnection(), EmbeddedSql.Read(typeof(InventoryDatabase).Assembly, "DevSeed.sql"), ct);
}
