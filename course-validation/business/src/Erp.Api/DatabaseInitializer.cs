using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Erp.Api;

// Applies each module's embedded Infrastructure/Sql/*.sql scripts on startup — the "migrations without EF"
// approach from Phase 3 §7. Scripts are idempotent (IF NOT EXISTS guards + CREATE OR ALTER PROCEDURE),
// so re-running on every boot is safe. Sales runs before Inventory (its seed references Sales items).
public static class DatabaseInitializer
{
    public static async Task RunAsync(WebApplication app)
    {
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");
        var connectionString = app.Configuration.GetConnectionString("ErpDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            log.LogWarning("No ErpDb connection string configured — skipping database initialization.");
            return;
        }

        try
        {
            var csb = new SqlConnectionStringBuilder(connectionString);
            var dbName = csb.InitialCatalog;

            // 1) Create the database if it doesn't exist (connect to master first).
            var masterCs = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString;
            await using (var master = new SqlConnection(masterCs))
            {
                await master.OpenAsync();
                await using var create = master.CreateCommand();
                create.CommandText = $"IF DB_ID(N'{dbName}') IS NULL CREATE DATABASE [{dbName}];";
                await create.ExecuteNonQueryAsync();
            }

            // 2) Apply each module's scripts, in module order, in filename order, batch by batch.
            var assemblies = new[]
            {
                typeof(Erp.Modules.Sales.DependencyInjection).Assembly,      // sales + [identity]
                typeof(Erp.Modules.Inventory.DependencyInjection).Assembly   // inventory
            };

            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            foreach (var assembly in assemblies)
            {
                var scripts = assembly.GetManifestResourceNames()
                    .Where(n => n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

                foreach (var name in scripts)
                {
                    var sql = ReadResource(assembly, name);
                    foreach (var batch in SplitBatches(sql))
                    {
                        await using var cmd = conn.CreateCommand();
                        cmd.CommandText = batch;
                        cmd.CommandTimeout = 120;
                        await cmd.ExecuteNonQueryAsync();
                    }
                    log.LogInformation("Applied SQL script {Script}", name);
                }
            }

            log.LogInformation("Database '{Db}' is ready.", dbName);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Database initialization failed. The API will still start, but data endpoints " +
                             "will error until SQL Server is reachable and the scripts apply.");
        }
    }

    private static string ReadResource(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // Split on lines that are just "GO" — the T-SQL batch separator (ADO.NET doesn't understand GO itself).
    private static IEnumerable<string> SplitBatches(string sql) =>
        Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
             .Select(b => b.Trim())
             .Where(b => b.Length > 0);
}
