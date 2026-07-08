# Phase 3b — Data Access: Connections & Transactions

> A short **bridge phase**. [Phase 3](03-database-schema.md) built the database — tables, stored procedures,
> seed data; from [Phase 4](04-jwt-auth.md) on, C# needs to *talk* to it. Before any handler can read a user
> or write an order, we need two small pieces of plumbing: a way to **open a connection**, and a way to group
> writes into an **all-or-nothing transaction**. We build both here, once, so every later phase just uses
> them.

---

> ⏱️ **Time for this phase:** ~30–45 min. Small and foundational — two interfaces, two implementations, and
> the one rule for choosing between them. Nothing here is business logic; it's the pipe every later phase runs
> its SQL through.

## 1. What we're building

- `IDbConnectionFactory` — hands out a database connection on demand (reads use it directly).
- `IUnitOfWork` / `IAppTransaction` — an explicit, **visible** transaction for writes that must be
  all-or-nothing.
- The **one rule** for which of the two to reach for, and the "throw = rollback" guarantee you'll lean on in
  every write handler from Phase 4 on.

## 2. Why a separate, tiny phase for this?

Two reasons.

**First, it's shared.** Auth ([Phase 4](04-jwt-auth.md)), Sales ([Phase 6](06-create-order.md)), and every
query slice ([Phase 10](10-queries-search.md)) all open connections and wrap writes. That's not a Sales
concern or an auth concern — it's the **platform's**, so it lives in `Erp.Shared.Infrastructure` and gets
explained once, here, instead of again in every phase that writes a row.

**Second — feel the pain it removes.** As always, the naive version first.

### Attempt 1 — `new SqlConnection(...)` scattered everywhere

Every handler news up its own connection with the connection string copy-pasted in. Change the string, add
retry logic, or switch databases, and you're editing dozens of files — and the connection string has leaked
into modules that should know nothing about SQL Server. A single **factory** fixes this: one place knows how
to make a connection; everyone else just asks for one.

### Attempt 2 — writes with no transaction

An order is a header **plus** its lines — several `INSERT`s. Without a transaction, a failure *after* the
header but *before* the last line leaves a **half-written order**: a header pointing at lines that don't
exist, with no way to undo it. Wrapping the whole write in one transaction makes it **atomic** — every row
commits together, or none does.

> 💡 **Analogy — the turnstile.** A transaction is a turnstile: you're either all the way through or still
> outside, never stuck halfway. `CommitAsync` is the click that lets you through; throw before it and you
> never entered — the database is untouched.

## 3. Packages

> 📦 This is the first phase that runs SQL from C#, so the data-access packages come in here (the connection
> string itself was set up in [Phase 3](03-database-schema.md)):
> ```bash
> dotnet add src/Shared/Erp.Shared.Infrastructure package Microsoft.Data.SqlClient
> dotnet add src/Shared/Erp.Shared.Infrastructure package Dapper
> ```
> Both live in `Shared.Infrastructure`: it's where the factory and unit of work create `SqlConnection`s, and
> where the shared kernel runs Dapper. (Each *module* adds its own `Dapper` reference when it writes its first
> repository — Sales does so in [Phase 6](06-create-order.md).) With Central Package Management on, each add
> writes a `<PackageVersion>` line into `Directory.Packages.props`.

## 4. The connection factory

`IDbConnectionFactory` hands out a connection; nobody news up a `SqlConnection` directly. The interface is a
**port** (any module may depend on it); the implementation lives in `Shared.Infrastructure` and is the *only*
place that knows we're on SQL Server.

`Erp.Shared.Infrastructure/Persistence/SqlConnectionFactory.cs`:

```csharp
public interface IDbConnectionFactory
{
    IDbConnection Create();   // returns an unopened SqlConnection; the caller opens it
}

public sealed class SqlConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public IDbConnection Create() => new SqlConnection(connectionString);
}
```

> 🔑 Only `SqlConnectionFactory` ever mentions `SqlConnection`. A module depends on `IDbConnectionFactory` and
> never learns which database engine is behind it — the same "depend on the interface" move as
> `ISalesOrderRepository` ([Phase 6](06-create-order.md)) or `IUserRepository` ([Phase 4](04-jwt-auth.md)).

## 5. When to use a transaction — the one rule

Not every database call needs a transaction, and wrapping a plain read in one just holds locks for no benefit.
This tutorial uses **exactly two** data-access paths, and picking the right one is a real skill:

| | **Read-only** (one or more `SELECT`s, no mutation) | **Write** (mutations that must be all-or-nothing) |
|---|---|---|
| Use | `IDbConnectionFactory.Create()` **directly** — open, query, done | `IUnitOfWork.BeginAsync()` → run writes on `tx.Transaction` → `CommitAsync` |
| Transaction? | **No.** | **Yes** — the whole unit commits or rolls back together. |
| Why | A single read has nothing to roll back; a transaction just adds locks and overhead for no gain. | Every row in the unit must succeed together, or none should exist. That atomicity is the entire point. |

> 🔑 **Rule of thumb:** *reads* grab a connection and release it; *writes* that touch more than one row (or
> more than one table) go through the unit of work. And read-only validation always happens **before** you
> open a transaction — never hold one open while you're still deciding.

## 6. The unit of work

`IUnitOfWork` gives a handler a **visible** transaction — the boundary is right there in the handler, not
hidden inside a framework. `Erp.Shared.Infrastructure/Persistence/UnitOfWork.cs`:

```csharp
public interface IUnitOfWork
{
    Task<IAppTransaction> BeginAsync(CancellationToken ct);
}

public interface IAppTransaction : IAsyncDisposable
{
    IDbTransaction Transaction { get; }        // hand this to each repository write
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
```

Every write handler you'll write from Phase 4 on uses the same three moves:

```csharp
await using var tx = await uow.BeginAsync(ct);   // 1) open
await repo.AddAsync(..., tx.Transaction, ct);    // 2) run each write ON that transaction
await tx.CommitAsync(ct);                          // 3) seal it
```

> ❓ **"How does a failure roll back? I don't see a `Rollback` call."** The `await using` disposes `tx` when
> the handler exits. Reach `CommitAsync` and the changes are saved. But if anything throws *before* the commit
> — a bad password, a credit-limit rule, a dropped connection — the handler exits early via the exception,
> `DisposeAsync` runs, and because we never committed, disposing the transaction **rolls it back
> automatically**. So **throw = rollback** falls out for free; you never write `Rollback` yourself. Every
> write from [Phase 4](04-jwt-auth.md) on leans on exactly this.

## 7. Register both

Both are platform services, registered once in `AddSharedInfrastructure` (they also appear in the consolidated
composition root in [Phase 11 §7](11-cross-cutting.md)):

```csharp
services.AddScoped<IDbConnectionFactory>(_ => new SqlConnectionFactory(config.GetConnectionString("ErpDb")!));
services.AddScoped<IUnitOfWork, UnitOfWork>();
```

> ℹ️ `config.GetConnectionString("ErpDb")` reads the `ConnectionStrings:ErpDb` value you set in
> [Phase 3](03-database-schema.md) — through the same `IConfiguration` pipeline detailed in
> [Phase 4 §7](04-jwt-auth.md), so it comes from `appsettings.Development.json` locally and an environment
> variable in production, with no code change.

## 8. Checkpoint — prove C# can reach the database ✅

You don't have a repository yet, so prove the *plumbing* with a throwaway read against Phase 3's seed. Drop a
temporary endpoint in `Program.cs`:

```csharp
app.MapGet("/db-ping", async (IDbConnectionFactory factory, CancellationToken ct) =>
{
    using var conn = factory.Create();
    var count = await conn.QuerySingleAsync<int>(
        new CommandDefinition("SELECT COUNT(*) FROM [identity].AppUser", cancellationToken: ct));
    return Results.Ok(new { users = count });
});
```

Run the app and call it:

```bash
curl http://localhost:5xxx/db-ping      # → { "users": 3 }
```

Three users (the `rep` / `manager` / `admin` seed from Phase 3) means the connection string, the factory, and
Dapper all work end-to-end. **Delete the `/db-ping` endpoint** once you've seen it green — real data access
starts in [Phase 4](04-jwt-auth.md), where `IUserRepository` becomes the first repository built on this
factory.

## 9. Common mistakes & best practices

- ❌ **`new SqlConnection(...)` inside a handler.** ✅ Inject `IDbConnectionFactory`; only the factory knows
  the engine and the connection string.
- ❌ **Wrapping a plain read in a transaction.** ✅ Reads use `Create()` directly; transactions are for
  all-or-nothing *writes*.
- ❌ **Opening the transaction, then doing validation inside it.** ✅ Validate first; `BeginAsync` last, so you
  don't hold locks while thinking.
- ❌ **Calling `Rollback` by hand — or forgetting to.** ✅ Don't; leaving the `using` scope without committing
  rolls back for you.
- ❌ **Leaking the connection string into a module.** ✅ It stays in the composition root; modules see only the
  interface.

---

## ✅ Check Yourself

1. What are the two data-access paths, and which one does a plain `SELECT` use?
2. Why must a write that inserts an order header *and* its lines happen inside a single transaction?
3. There's no `Rollback` call anywhere — so how does a failed handler undo its writes?
4. Why does a module depend on `IDbConnectionFactory` instead of newing up a `SqlConnection` itself?

> 👉 Next: [Phase 4 — JWT authentication & authorization](04-jwt-auth.md).
