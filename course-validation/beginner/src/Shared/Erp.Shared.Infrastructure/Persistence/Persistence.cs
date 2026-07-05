using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Erp.Shared.Infrastructure.Persistence;

// ── Interfaces (given in Phase 4) ──────────────────────────────────────
public interface IDbConnectionFactory
{
    IDbConnection Create();   // returns an OPEN SqlConnection
}

public interface IUnitOfWork
{
    Task<IAppTransaction> BeginAsync(CancellationToken ct);   // opens a connection + starts a transaction
}

public interface IAppTransaction : IAsyncDisposable
{
    IDbTransaction Inner { get; }                // the raw transaction to hand to repositories
    Task CommitAsync(CancellationToken ct);      // make the changes permanent
}

// ── SQL Server implementations (NOT shown in the plan — inferred) ──────
public sealed class SqlConnectionFactory(IConfiguration config) : IDbConnectionFactory
{
    private readonly string _cs = config.GetConnectionString("Erp")
        ?? throw new InvalidOperationException("Missing connection string 'Erp'.");

    public IDbConnection Create()
    {
        var conn = new SqlConnection(_cs);
        conn.Open();
        return conn;
    }
}

public sealed class UnitOfWork(IDbConnectionFactory factory) : IUnitOfWork
{
    public Task<IAppTransaction> BeginAsync(CancellationToken ct)
    {
        var conn = (SqlConnection)factory.Create();
        var tx = conn.BeginTransaction();
        return Task.FromResult<IAppTransaction>(new AppTransaction(conn, tx));
    }
}

internal sealed class AppTransaction(SqlConnection conn, SqlTransaction tx) : IAppTransaction
{
    private bool _committed;
    public IDbTransaction Inner => tx;

    public async Task CommitAsync(CancellationToken ct)
    {
        await tx.CommitAsync(ct);
        _committed = true;
    }

    public async ValueTask DisposeAsync()
    {
        // If we never committed (e.g. a rule threw), disposing rolls it back — "throw = rollback" for free.
        if (!_committed)
        {
            try { await tx.RollbackAsync(); } catch { /* connection may already be gone */ }
        }
        await tx.DisposeAsync();
        await conn.DisposeAsync();
    }
}
