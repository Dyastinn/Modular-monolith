using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Sales.Infrastructure;

internal sealed class SalesDatabase(SalesDbContext db) : IModuleDatabase
{
    public string Module => "Sales";

    public Task MigrateAsync(CancellationToken ct) => db.Database.MigrateAsync(ct);

    public Task SeedDevelopmentDataAsync(CancellationToken ct) =>
        EmbeddedSql.ExecuteAsync(
            db.Database.GetDbConnection(), EmbeddedSql.Read(typeof(SalesDatabase).Assembly, "DevSeed.sql"), ct);
}
