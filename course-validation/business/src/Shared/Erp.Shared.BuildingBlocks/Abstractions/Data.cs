using System.Data.Common;
using System.Reflection;

namespace Erp.Shared.BuildingBlocks;

// Reads: a fresh connection for one Dapper query. The caller disposes it.
public interface IDbConnectionFactory
{
    DbConnection Create();
}

// Writes: the ONE connection every module's DbContext uses during a request. Because Sales and Inventory
// share it, Inventory can join a transaction Sales started (see IStockReservation).
public interface ISharedConnection
{
    DbConnection Connection { get; }
}

// What the host needs from each module's database at startup: apply migrations, and (dev/test only) seed.
public interface IModuleDatabase
{
    string Module { get; }
    Task MigrateAsync(CancellationToken ct);
    Task SeedDevelopmentDataAsync(CancellationToken ct);
}

// Reads a .sql file embedded in a module assembly — used by migrations to ship stored procedures and by the
// dev seeders. Matches on the file name, so the folder it lives in doesn't matter.
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

    // Runs a whole script as one plain command. Deliberately NOT EF's ExecuteSqlRaw: that treats { and } as
    // format placeholders, so a brace anywhere in the script (a comment, a JSON literal) breaks it.
    public static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken ct)
    {
        var wasOpen = connection.State == System.Data.ConnectionState.Open;
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
