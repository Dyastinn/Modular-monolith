using System.Data;
using System.Data.Common;
using System.Reflection;

namespace Erp.Shared.BuildingBlocks;

// A new connection per Dapper read; the caller disposes it.
public interface IDbConnectionFactory
{
    DbConnection Create();
}

// The one connection every module DbContext uses during a request, so a module can join another's transaction.
public interface ISharedConnection
{
    DbConnection Connection { get; }
}

// Migrations and seeding take a connection because they run as the schema owner, not as the app's role.
public interface IModuleDatabase
{
    string Module { get; }
    string Schema { get; }
    Task MigrateAsync(DbConnection ownerConnection, CancellationToken ct);
    Task SeedDevelopmentDataAsync(DbConnection ownerConnection, CancellationToken ct);
}

public static class EmbeddedSql
{
    public static string Read(Assembly assembly, string fileName)
    {
        var name = assembly.GetManifestResourceNames()
            .SingleOrDefault(n => n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded SQL '{fileName}' not found in {assembly.GetName().Name}.");
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // Not EF's ExecuteSqlRaw: that treats { and } as placeholders and breaks on any brace in the script.
    public static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken ct)
    {
        var wasOpen = connection.State == ConnectionState.Open;
        if (!wasOpen) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (!wasOpen) await connection.CloseAsync();
        }
    }
}
