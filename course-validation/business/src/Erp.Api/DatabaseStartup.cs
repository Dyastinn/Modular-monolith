using Erp.Shared.BuildingBlocks;
using Erp.Shared.Infrastructure;

namespace Erp.Api;

// Brings every module's schema up to date at startup, then (dev/test only) loads sample data.
// Both are switched on in appsettings.Development.json and OFF by default, so production never seeds demo
// users. Production installs apply migrations as an explicit deployment step instead.
internal static class DatabaseStartup
{
    public static async Task InitializeDatabaseAsync(this WebApplication app, CancellationToken ct = default)
    {
        var settings = app.Configuration.GetSection("Database");
        if (!settings.GetValue<bool>("MigrateOnStartup")) return;

        await PostgresDatabase.EnsureExistsAsync(app.Configuration.GetConnectionString("ErpDb")!, ct);

        await using var scope = app.Services.CreateAsyncScope();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseStartup");
        var modules = scope.ServiceProvider.GetServices<IModuleDatabase>().ToList();

        foreach (var module in modules)
        {
            await module.MigrateAsync(ct);
            log.LogInformation("{Module} schema is up to date", module.Module);
        }

        if (!settings.GetValue<bool>("SeedDevelopmentData")) return;

        foreach (var module in modules)   // registration order: Identity, Sales, Inventory
        {
            await module.SeedDevelopmentDataAsync(ct);
            log.LogInformation("{Module} development data seeded", module.Module);
        }
    }
}
