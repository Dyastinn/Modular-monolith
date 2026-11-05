using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Identity.Infrastructure;

internal sealed class IdentityDatabase(IdentityDbContext db) : IModuleDatabase
{
    public string Module => "Identity";

    public Task MigrateAsync(CancellationToken ct) => db.Database.MigrateAsync(ct);

    public Task SeedDevelopmentDataAsync(CancellationToken ct) =>
        EmbeddedSql.ExecuteAsync(
            db.Database.GetDbConnection(), EmbeddedSql.Read(typeof(IdentityDatabase).Assembly, "DevSeed.sql"), ct);
}
