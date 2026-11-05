using System.Data.Common;
using Erp.Shared.BuildingBlocks;
using Npgsql;

namespace Erp.Shared.Infrastructure;

// The only types in the solution that name Npgsql connections. NpgsqlDataSource is created once for the app
// and owns the connection pool; every connection below comes from it.

public sealed class PostgresConnectionFactory(NpgsqlDataSource dataSource) : IDbConnectionFactory
{
    public DbConnection Create() => dataSource.CreateConnection();
}

// Scoped: one per request. Every module's DbContext is built on this same connection object. EF opens and
// closes it as needed; the DI scope disposes it at the end of the request.
public sealed class SharedPostgresConnection(NpgsqlDataSource dataSource) : ISharedConnection, IAsyncDisposable, IDisposable
{
    public DbConnection Connection { get; } = dataSource.CreateConnection();

    public ValueTask DisposeAsync() => Connection.DisposeAsync();
    public void Dispose() => Connection.Dispose();
}

public static class PostgresDatabase
{
    // Creates the database named in the connection string if it isn't there yet — first run of a new install,
    // or a fresh test database. Connects to the built-in "postgres" maintenance database to do it.
    public static async Task EnsureExistsAsync(string connectionString, CancellationToken ct = default)
    {
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres", Pooling = false };

        await using var conn = new NpgsqlConnection(admin.ConnectionString);
        await conn.OpenAsync(ct);

        await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", conn);
        exists.Parameters.AddWithValue("name", target.Database!);
        if (await exists.ExecuteScalarAsync(ct) is not null) return;

        // A database name can't be a parameter; quote it as an identifier instead.
        var quoted = "\"" + target.Database!.Replace("\"", "\"\"") + "\"";
        await using var create = new NpgsqlCommand($"CREATE DATABASE {quoted}", conn);
        await create.ExecuteNonQueryAsync(ct);
    }
}
