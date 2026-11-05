using Erp.Shared.BuildingBlocks;
using Erp.Shared.Infrastructure;
using Npgsql;

namespace Erp.Api;

// Off by default (appsettings.json); on in Development and tests. Production applies migrations as a
// deployment step instead, so a bad migration can't take the site down on a routine restart.
internal static class DatabaseStartup
{
    public static async Task InitializeDatabaseAsync(this WebApplication app, CancellationToken ct = default)
    {
        var settings = app.Configuration.GetSection("Database");
        if (!settings.GetValue<bool>("MigrateOnStartup")) return;

        // Migrations need the schema owner; the app itself may run as a role that can only read and write rows.
        var ownerConnectionString = app.Configuration.GetConnectionString("ErpDbMigrations") is { Length: > 0 } owner
            ? owner
            : app.Configuration.GetConnectionString("ErpDb")!;
        await PostgresDatabase.EnsureExistsAsync(ownerConnectionString, ct);

        await using var ownerConnection = new NpgsqlConnection(ownerConnectionString);
        await ownerConnection.OpenAsync(ct);
        await using var scope = app.Services.CreateAsyncScope();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseStartup");
        var modules = scope.ServiceProvider.GetServices<IModuleDatabase>().ToList();

        foreach (var module in modules)
        {
            await module.MigrateAsync(ownerConnection, ct);
            log.LogInformation("{Module} schema is up to date", module.Module);
        }

        if (!settings.GetValue<bool>("SeedDevelopmentData")) return;

        foreach (var module in modules)
            await module.SeedDevelopmentDataAsync(ownerConnection, ct);
        log.LogInformation("Development data seeded");
    }
}
