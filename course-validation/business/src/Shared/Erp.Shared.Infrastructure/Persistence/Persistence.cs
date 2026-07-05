using System.Data;
using Microsoft.Data.SqlClient;

namespace Erp.Shared.Infrastructure;

// Hands out connections. Query handlers call Create() directly for reads; the unit of work uses it to
// start a transaction for writes (Phase 6 §7).
public interface IDbConnectionFactory
{
    IDbConnection Create();
}

public sealed class SqlConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public IDbConnection Create() => new SqlConnection(connectionString);
}

// Gives the handler a visible transaction boundary. throw = rollback (no explicit Rollback call).
public interface IUnitOfWork
{
    Task<IAppTransaction> BeginAsync(CancellationToken ct);
}

public interface IAppTransaction : IAsyncDisposable
{
    IDbTransaction Transaction { get; }
    Task CommitAsync(CancellationToken ct);
}

public sealed class UnitOfWork(IDbConnectionFactory factory) : IUnitOfWork
{
    public async Task<IAppTransaction> BeginAsync(CancellationToken ct)
    {
        var conn = factory.Create();
        await ((SqlConnection)conn).OpenAsync(ct);
        var tx = conn.BeginTransaction();
        return new AppTransaction(conn, tx);
    }
}

public sealed class AppTransaction(IDbConnection conn, IDbTransaction tx) : IAppTransaction
{
    public IDbTransaction Transaction => tx;
    public Task CommitAsync(CancellationToken ct) { tx.Commit(); return Task.CompletedTask; }

    public async ValueTask DisposeAsync()   // if we never committed, this rolls back
    {
        tx.Dispose();
        if (conn is SqlConnection sc) await sc.DisposeAsync(); else conn.Dispose();
    }
}
