# Phase 3b — Data Access: Connections & Transactions

> A short **bridge phase**. [Phase 3](03-database-schema.md) built the database — tables, stored procedures,
> seed data; from [Phase 4](04-jwt-auth.md) on, C# needs to *talk* to it. Before any handler can read a user
> or write an order, we need two small pieces of plumbing: a way to **open a connection**, and a way to group
> writes into an **all-or-nothing transaction**. We build the first here, once, and learn the second — plain
> ADO.NET transactions — so every later phase just uses them.

---

> ⏱️ **Time for this phase:** ~30–45 min. Small and foundational — one interface, one implementation, and the
> one rule for choosing between a bare connection and a transaction. Nothing here is business logic; it's the
> pipe every later phase runs its SQL through.

## 1. What we're building

- `IDbConnectionFactory` — hands out a database connection on demand (reads use it directly).
- The **transaction pattern** for writes that must be all-or-nothing — an `IDbTransaction` opened right in the
  handler with `BeginTransaction()`, committed on success, rolled back on failure.
- The **one rule** for choosing between them, and the "commit on success, roll back on throw" shape you'll
  reuse in every write handler from Phase 4 on.

## 2. Why a separate, tiny phase for this?

Two reasons.

**First, it's shared.** Auth ([Phase 4](04-jwt-auth.md)), Sales ([Phase 6](06-create-order.md)), and every
query slice ([Phase 10](10-queries-search.md)) all open connections and wrap writes. That's not a Sales
concern or an auth concern — it's the **platform's**, so the connection factory lives in
`Erp.Shared.Infrastructure` and gets explained once, here, instead of again in every phase that writes a row.

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
> outside, never stuck halfway. `Commit()` is the click that lets you through; throw before it and you never
> entered — you `Rollback()`, and the database is untouched.

## 3. Packages

> 📦 This is the first phase that runs SQL from C#, so the data-access packages come in here (the connection
> string itself was set up in [Phase 3](03-database-schema.md)):
> ```bash
> dotnet add src/Shared/Erp.Shared.Infrastructure package Microsoft.Data.SqlClient
> dotnet add src/Shared/Erp.Shared.Infrastructure package Dapper
> ```
> Both live in `Shared.Infrastructure`: it's where the factory creates `SqlConnection`s, and where the shared
> kernel runs Dapper. (Each *module* adds its own `Dapper` reference when it writes its first repository —
> Sales does so in [Phase 6](06-create-order.md).) With Central Package Management on, each add writes a
> `<PackageVersion>` line into `Directory.Packages.props`.

## 4. The connection factory

`IDbConnectionFactory` hands out a database connection; nobody news up a `SqlConnection` directly.

The interface is a **port** — any module may depend on it without knowing which database engine sits behind it. The implementation is an **adapter** that lives in `Shared.Infrastructure`, the only project that knows we're using SQL Server.

```
Erp.Shared.Common
└── Data
    └── IDbConnectionFactory.cs

Erp.Shared.Infrastructure
└── Data
    └── SqlConnectionFactory.cs
```

`Erp.Shared.Common/Data/IDbConnectionFactory.cs`:

```csharp
using System.Data.Common;

namespace Erp.Shared.Common.Data;

public interface IDbConnectionFactory
{
    DbConnection Create();   // returns an unopened connection; the caller opens it
}
```

> ❓ **"Why `DbConnection`, not `IDbConnection`?"** `IDbConnection` only has a *synchronous* `Open()`. We want
> `await conn.OpenAsync(ct)` so a waiting request doesn't tie up a thread. `DbConnection` (from
> `System.Data.Common`) is the ADO.NET base class that adds the async methods — and it's still an *abstract*
> type, not `SqlConnection`, so it stays a real port: no module learns which engine is behind it. Its
> `BeginTransaction()` returns a `DbTransaction`, which is itself an `IDbTransaction`.

`Erp.Shared.Infrastructure/Data/SqlConnectionFactory.cs`:

```csharp
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Erp.Shared.Common.Data;

namespace Erp.Shared.Infrastructure.Data;

public sealed class SqlConnectionFactory(string connectionString)
    : IDbConnectionFactory
{
    public DbConnection Create()
        => new SqlConnection(connectionString);
}
```

> 🔑 Only `SqlConnectionFactory` ever mentions `SqlConnection`. Every other part of the application depends only on `IDbConnectionFactory`, so it never learns which database engine sits behind the interface. Today the implementation happens to create a `SqlConnection`; tomorrow it could create a PostgreSQL connection instead, without changing any application code.
>
> This is the same dependency inversion you'll use throughout the course with abstractions such as `IUserRepository` and `ISalesOrderRepository`: **the application owns the contract, while infrastructure provides the implementation.**

## 5. When to use a transaction — the one rule

Not every database call needs a transaction, and wrapping a plain read in one just holds locks for no benefit.
This tutorial uses **exactly two** data-access paths, and picking the right one is a real skill:

| | **Read-only** (one or more `SELECT`s, no mutation) | **Write** (mutations that must be all-or-nothing) |
|---|---|---|
| Use | `factory.Create()` **directly** — open, query, done | `factory.Create()` → `OpenAsync` → `BeginTransaction()` → run writes → `Commit()` |
| Transaction? | **No.** | **Yes** — the whole unit commits or rolls back together. |
| Why | A single read has nothing to roll back; a transaction just adds locks and overhead for no gain. | Every row in the unit must succeed together, or none should exist. That atomicity is the entire point. |

> 🔑 **Rule of thumb:** *reads* grab a connection and release it; *writes* that touch more than one row (or
> more than one table) run inside a transaction. And read-only validation always happens **before** you
> `BeginTransaction()` — never hold one open while you're still deciding.

## 6. Transactions

A read is a single `SELECT`. It changes nothing, so there is nothing to undo — open a connection, query,
release it. No transaction, no locks held for a decision you've already made.

A write is different. Saving an order is a header **plus** its lines — several `INSERT`s that only make sense
together. If the second `INSERT` fails, the first must vanish too. That "all of them or none of them"
guarantee is what a **transaction** buys you.

### `IDbTransaction`, `Commit()`, and `Rollback()`

`BeginTransaction()` on an open connection hands back an `IDbTransaction`. Think of it as a **scratch space**
the database keeps for you: every statement you run *on that transaction* is provisional — visible to you,
but not yet real for anyone else.

- **`Commit()`** makes every statement in the scratch space real, all at once. The turnstile clicks; you're
  through.
- **`Rollback()`** throws the scratch space away. Every provisional row disappears as if you'd never touched
  the database.

You attach each write to the transaction by passing it into the repository call (Dapper takes it as a
parameter). That's how the database knows those `INSERT`s belong to the same unit.

### Why `using` / `await using` matters

A connection and a transaction are both **unmanaged resources** — a real socket to SQL Server and server-side
state. If you forget to release them, you leak a connection from the pool; do it enough and the app grinds to
a halt waiting for a free one.

`using` guarantees release: the moment control leaves the block — normal exit *or* an exception — `Dispose()`
runs. And disposing a transaction that was **never committed rolls it back** — so `using` is also your safety
net: if something throws past your `catch`, the half-written scratch space is still discarded on the way out.

> ❓ **"Shouldn't that be `await using`?"** There is an async form — `await using` calls `DisposeAsync()`, which
> can close the socket without blocking a thread. It's the better choice when disposal genuinely does I/O. We
> use plain `using` everywhere in this tutorial for one reason: **consistency you can read**. Handlers already
> mix `await`ed repository calls with a synchronous `Commit()`, and adding `await` to the `using` lines makes
> the transaction boundary harder to spot, not easier. The connection returns to the pool either way. If you
> later profile a thread-starvation problem under real load, switching these to `await using` is a
> mechanical, safe change — just do it everywhere at once.

### The pattern every write handler uses

```csharp
using var conn = factory.Create();
await conn.OpenAsync(ct);

using var tx = conn.BeginTransaction();

try
{
    await repo.AddHeaderAsync(..., tx, ct);
    await repo.AddLinesAsync(..., tx, ct);

    tx.Commit();
}
catch
{
    tx.Rollback();
    throw;
}
```

Read it top to bottom: open a connection, start a transaction, run every write **on that transaction**, and
`Commit()` only once they've all succeeded. If any line throws, control jumps to the `catch`, we `Rollback()`
the provisional rows, and `throw;` rethrows so the caller still sees the failure (Phase 11's exception
handler turns it into the right HTTP response).

> ❓ **"Isn't the `catch` redundant if `using` already rolls back?"** Almost — disposing the uncommitted
> transaction *would* roll back on its own. The explicit `Rollback()` earns its place by making the intent
> **visible in the handler** (a reader sees exactly where the boundary is) and by rolling back *immediately*,
> before any later cleanup runs. The `using` remains the backstop for anything the `catch` can't reach. Belt
> and suspenders — cheap, and honest about what happens on failure.

> 🔑 **Commit on success, roll back on throw.** That's the whole shape. Every write from
> [Phase 4](04-jwt-auth.md) on is a variation of these ten lines — the only thing that changes is which
> repository methods run between `BeginTransaction()` and `Commit()`.

> ❓ **"Shouldn't this be hidden behind an `IUnitOfWork`?"** You'll see that abstraction in a lot of codebases:
> an interface with a `SaveChangesAsync()` that quietly owns the transaction, so handlers never mention one.
> We don't use it, and the reason is worth sitting with. The transaction boundary is **the most consequential
> decision in a write operation** — it's what decides whether a failure leaves you with half an order. Hiding
> it behind a method named `SaveChanges` doesn't remove the decision; it removes your ability to *see* the
> decision while reading the handler. Six months later, nobody can tell you where the transaction starts
> without opening three other files.
>
> There's a second, sharper reason. A `IDbTransaction` is bound to **one open connection to one database**. The
> moment a "unit of work" spans two connections — two databases, a database and a message broker — the
> abstraction quietly stops meaning what its name promises, and it fails in ways that look like magic. Keeping
> the transaction where you opened it keeps that limit visible. (This is also why the cross-module contract in
> [Phase 7](07-submit-reserve-concurrency.md) takes an `IDbTransaction` parameter: Inventory has to enlist in
> *the caller's* transaction, and it can only do that because there is one to hand it.)

## 7. Register the factory

The factory is a platform service, registered once in `AddSharedInfrastructure` (it also appears in the
consolidated composition root in [Phase 11 §7](11-cross-cutting.md)):

```csharp
services.AddScoped<IDbConnectionFactory>(_ => new SqlConnectionFactory(config.GetConnectionString("ErpDb")!));
```

There's nothing else to register — a transaction isn't a service, it's something a handler opens on the
connection it just asked the factory for.

> ℹ️ `config.GetConnectionString("ErpDb")` reads the `ConnectionStrings:ErpDb` value you set in
> [Phase 3](03-database-schema.md) — through the same `IConfiguration` pipeline detailed in
> [Phase 4 §7](04b-jwt-login.md), so it comes from `appsettings.Development.json` locally and an environment
> variable in production, with no code change.

## 7b. Why each piece exists

| Piece | Why it exists | Where it lives |
|---|---|---|
| `IDbConnectionFactory` | So a module can ask for a connection without learning which engine or connection string is behind it. The **port**. | `Erp.Shared.Common/Data/` — no dependencies, so even a pure module can reference it. |
| `SqlConnectionFactory` | The one class in the solution allowed to say the words `SqlConnection`. The **adapter**. | `Erp.Shared.Infrastructure/Data/` — the only project that knows we chose SQL Server. |
| `DbConnection` (not `IDbConnection`) | It's the abstract ADO.NET base class that has `OpenAsync`, so a waiting request doesn't pin a thread. | Returned by `Create()`. |
| `IDbTransaction` | The unit of atomicity, opened **in the handler** so the boundary is visible in the code that owns it. | Never registered in DI — a handler opens it on the connection it just got. |

Notice there is no repository, no `IUnitOfWork`, and no base class here. Four small pieces, and two of them
are types ADO.NET already gave us.

## 8. Checkpoint — prove C# can reach the database ✅

You don't have a repository yet, so prove the *plumbing* with a throwaway read against Phase 3's seed. Drop a
temporary endpoint in `Program.cs` — plain ADO.NET, no Dapper, because we haven't met Dapper's API yet and
this is really a test of the factory and the connection string:

```csharp
// Program.cs — temporary. Add `using Erp.Shared.Common.Data;` at the top.
app.MapGet("/db-ping", async (IDbConnectionFactory factory, CancellationToken ct) =>
{
    using var conn = factory.Create();          // the factory hands back an unopened connection
    await conn.OpenAsync(ct);                   // ...and we open it

    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT COUNT(*) FROM [identity].AppUser";
    var count = (int)(await cmd.ExecuteScalarAsync(ct))!;

    return Results.Ok(new { users = count });
});
```

Run the app and call it:

```bash
curl http://localhost:5234/db-ping      # → { "users": 3 }   (use whatever port it printed)
```

Three users (the `rep` / `manager` / `admin` seed from Phase 3) means the connection string, the factory, and
the registration in `AddSharedInfrastructure` all work end-to-end. That's the whole pipe: C# can reach the
database. **Delete the `/db-ping` endpoint** once you've seen it green — real data access starts in
[Phase 4](04-jwt-auth.md), where `IUserRepository` becomes the first repository built on this factory, and
where you meet Dapper's API for the first time.

## 9. Common mistakes & best practices

- ❌ **`new SqlConnection(...)` inside a handler.** ✅ Inject `IDbConnectionFactory`; only the factory knows
  the engine and the connection string.
- ❌ **Wrapping a plain read in a transaction.** ✅ Reads use `Create()` directly; transactions are for
  all-or-nothing *writes*.
- ❌ **Calling `BeginTransaction()`, then doing validation inside it.** ✅ Validate first; open the transaction
  last, so you don't hold locks while thinking.
- ❌ **Forgetting the `using` on the connection or transaction.** ✅ Both are unmanaged resources; `using` /
  `await using` guarantees they're released — and disposing an uncommitted transaction rolls it back.
- ❌ **Committing in a `catch`, or swallowing the exception without `throw;`.** ✅ `Commit()` only on the happy
  path; `Rollback(); throw;` on failure so the caller still learns it failed.
- ❌ **Leaking the connection string into a module.** ✅ It stays in the composition root; modules see only the
  interface.

---

## ✅ Check Yourself

1. What are the two data-access paths, and which one does a plain `SELECT` use?
2. Why must a write that inserts an order header *and* its lines happen inside a single transaction?
3. What does `Commit()` do, and what happens to your rows if you `Rollback()` instead — or never commit at all?
4. Why does a module depend on `IDbConnectionFactory` instead of newing up a `SqlConnection` itself?

---

← Previous: [Phase 3 — Database: schema, stored procedures, seed data](03-database-schema.md) · [Tutorial overview](00-START-HERE.md) · Next: [Phase 4 (Part 1 of 5) — Passwords & Access Tokens](04-jwt-auth.md) →
