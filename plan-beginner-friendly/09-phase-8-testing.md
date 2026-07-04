# Phase 8 — Testing: Three Levels, Each With a Job

> 📄 Expert version: [`../plan-Opus/09-phase-8-testing.md`](../plan-Opus/09-phase-8-testing.md)

**In one sentence:** We write automated tests at three levels — fast rule-checks, real-database checks, and
whole-workflow checks — and learn what each level is *for* (and what it can't catch).

**What you'll build:** Domain/validator unit tests, handler+proc integration tests against a real SQL Server
test database, and two end-to-end workflow tests (including the cross-module submit → reserve path).
Separate Development/Testing connection strings. No Docker.

**Before you start:** Phases 3–7. The system works end-to-end when you run it manually.

---

## Step 1 — Why three levels?

Each level catches a *different* kind of bug and runs at a different speed:

| Level | What it tests | Uses a real DB? | Catches | Speed |
|---|---|---|---|---|
| **Unit** | Domain rules, validators | No | Wrong business logic; invalid-state bugs | milliseconds ⚡ |
| **Integration** | Handler + repository + stored proc + transaction | **Yes** | Broken SQL, mapping errors, bad transaction/concurrency behavior | slow-ish 🐢 |
| **End-to-end** | A whole workflow through the API | Yes | Wiring/composition errors — the parts don't fit together | slowest 🐌 |

You want a **pyramid**: lots of fast unit tests, fewer integration tests, a handful of e2e tests.

> 🔑 **A design signal hidden in here:** if you need a *database* to test a *business rule*, the rule is in
> the wrong place. It should be in the domain, where a millisecond-fast unit test can reach it. This test
> structure quietly tells you when your design has drifted.

## Step 2 — Unit tests (domain + validators, no database)

This is the payoff of the rich domain from Phase 3. No database mocks, no fixtures — just plain objects. They
live in `Erp.Modules.Sales.UnitTests`:

```csharp
[Fact]
public void Submitting_a_non_draft_order_throws()
{
    var order = SalesOrder.Create(new CustomerId(1));
    order.AddLine(new ProductId(1), new Quantity(1), new Money(10m));
    order.Submit(approvalGranted: true);                 // Draft → Submitted
    var act = () => order.Submit(approvalGranted: true);  // Submitted → Submitted is illegal
    Assert.Throws<DomainException>(act);
}

[Fact]
public void Order_over_threshold_requires_approval()
{
    var order = SalesOrder.Create(new CustomerId(1));
    order.AddLine(new ProductId(1), new Quantity(1), new Money(60_000m));
    Assert.Throws<DomainException>(() => order.Submit(approvalGranted: false));
}
```

### Reading a test line by line

```csharp
[Fact]                                                    // (a)
public void Submitting_a_non_draft_order_throws()         // (b)
{
    var order = SalesOrder.Create(new CustomerId(1));     // (c) Arrange
    order.AddLine(new ProductId(1), new Quantity(1), new Money(10m));
    order.Submit(approvalGranted: true);                  // (c) put it in Submitted state

    var act = () => order.Submit(approvalGranted: true);  // (d) Act (captured, not run yet)

    Assert.Throws<DomainException>(act);                  // (e) Assert
}
```

- **(a) `[Fact]`** — tells the runner "this method is a test."
- **(b) The name** *is documentation.* It reads like a sentence describing the rule.
- **(c) Arrange** — set up the world (create, add a line, submit so it's now `Submitted`).
- **(d) Act** — do the thing you're testing, *captured* in `act` so we can check that *calling it* throws.
- **(e) Assert** — `Assert.Throws<DomainException>` means "I expect this to throw; fail otherwise."

Most tests follow this **Arrange → Act → Assert** rhythm. Also unit-test validators and value objects
(`Money`/`Quantity` reject bad values). These run in milliseconds and should be the **bulk** of your tests.
*(Analogy: tasting the sauce by itself.)*

> ❓ **"What's the point of testing code I already know works?"** (1) You know it works *today* — a test
> proves it *still* works after someone changes the code next month (catching a *regression*). (2) Writing
> the test forces you to state the rule precisely, often revealing a case you hadn't thought about. Tests
> are a safety net for the *you* of six months from now.

## Step 3 — Integration tests (a real SQL Server, a separate database)

Per the brief: **no Docker**, a real SQL Server, separate connection strings.

- **`appsettings.Development.json`** → your Development database (manual runs).
- **`appsettings.Testing.json`** (or `ConnectionStrings__Testing` env/secret) → a dedicated **test**
  database. **Never point tests at Development.**

**Managing the schema:** keep each module's schema and procs as ordered `.sql` scripts in its
`Infrastructure/Sql/` folder (`sales`, `inventory`). A test fixture applies *all* modules' scripts to the
test database before the run. Bonus: this doubles as your "migrations" story (Step 6).

**Keeping tests from polluting each other:** run each test inside a transaction that gets **rolled back** at
teardown. *(Analogy: set the table, eat, then reset it exactly as it was.)*

```csharp
public sealed class SubmitSalesOrderTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fx;
    public SubmitSalesOrderTests(TestDatabaseFixture fx) => _fx = fx;

    [Fact]
    public async Task Reserving_more_than_available_fails_atomically()
    {
        // Arrange: seed the product with OnHand=5, Reserved=0 in the test database.

        // Act: fire TWO reservations of 5 at (nearly) the same time. The key: each runs in — and COMMITS —
        // its OWN transaction, so this is a real race, not a rollback (see the caveat below).
        var a = ReserveInOwnCommittedTransaction(productId: 1, qty: 5);
        var b = ReserveInOwnCommittedTransaction(productId: 1, qty: 5);
        bool[] results = await Task.WhenAll(a, b);

        // Assert: exactly one won; the other saw "not enough stock".
        Assert.Equal(1, results.Count(won => won));
        Assert.Equal(5, await GetReserved(productId: 1));   // never 10 — the atomic UPDATE held the line
    }

    // A tiny test helper: open a real connection, reserve through the REAL contract, and COMMIT.
    private async Task<bool> ReserveInOwnCommittedTransaction(long productId, int qty)
    {
        await using var conn = new SqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
        var stock = new StockReservationService();                // the real Phase 5 implementation
        var reserved = await stock.TryReserveAsync(productId, qty, tx, default);
        await tx.CommitAsync();                                    // COMMIT so the race is observable
        return reserved;
    }
    // GetReserved(productId): a one-line Dapper SELECT of the Reserved column. (Body omitted.)
}
```

> 💡 Why the real `StockReservationService` and not a fake? **A fake can't have a race condition**, so it
> would prove nothing. This test only means something against the actual database and the actual atomic
> proc. (The business tutorial's [Phase 12](../plan-business-tutorial/12-testing.md) shows the same test
> fully fleshed out.)

> 🌟 **That concurrency test is the single highest-value test in the whole suite.** It proves Phase 5's
> atomic SQL guard works — a bug unit tests *structurally cannot* catch (they have no concurrency). It also
> exercises the cross-module boundary: Sales' path reserving through Inventory's contract for real.

> ⚠️ **Caveat:** the "roll back after each test" trick can't observe *committed* concurrency behavior. The
> reservation-race test must **commit** and clean up manually.

**What integration tests catch that unit tests can't:** that stored procedures are valid SQL, that Dapper's
mapping (including the strongly-typed ID handlers) round-trips, that transactions commit/roll back as
intended, and that the optimistic-concurrency version check rejects stale updates.

## Step 4 — End-to-end tests (whole workflows)

Use `WebApplicationFactory<Program>` to spin up the real API in-memory against the test database, and drive
it over HTTP. Two required workflows:

**Workflow 1 — create → submit → verify (crosses the module wall):**
1. `POST /sales-orders` (create a draft) → capture the id.
2. `POST /sales-orders/{id}/submit`.
3. Assert HTTP success; `GET /sales-orders/{id}` shows `Submitted`; `inventory.Stock` shows the reservation;
   the DB row has the expected status and a bumped `rowversion`.

**Workflow 2 — create → cancel → verify:**
1. `POST /sales-orders` → id.
2. `POST /sales-orders/{id}/cancel`.
3. Assert the order is `Cancelled` and the reservation was released (through the contract).

E2E tests are few *on purpose* — slow and broad. Their job: catch *composition* failures (DI not wired, an
endpoint mis-mapped, a proc name typo, a module not registered) that lower levels miss. *(Analogy: serving
the whole meal to a guest.)*

## Step 5 — The decision rule: what to test where

- A **business rule** → unit test on the domain. If you can't test it without a database, move the rule into
  the domain.
- **SQL / mapping / transactions / concurrency / the cross-module contract** → integration test.
- **"Do the parts connect?"** → one e2e per critical workflow.

## Step 6 — "Migrations" without an ORM

- **Ordered SQL scripts + a runner (recommended):** `Sales/Infrastructure/Sql/001_schema.sql`, etc., applied
  in order, tracked in a `SchemaVersions` table. Simple, transparent, versionable — and the *same* scripts
  your test fixture applies. **DbUp** (free) does this for you.
- **SSDT / .dacpac:** declarative diff-based deploys; heavier tooling.
- **Manual scripts:** fine for learning, doesn't scale to a team.

Pick ordered-scripts-with-a-runner: one source of truth for schema, procs, tests, *and* deploys.

## What to produce in this phase

- `Erp.Modules.Sales.UnitTests`: submit/cancel/complete transitions, the approval threshold, value objects,
  validators (+ optional handler tests with faked repositories and a fake `IStockReservation`).
- `Erp.Integration.Tests`: a `TestDatabaseFixture` (applies each module's `Sql/` scripts to the Testing DB),
  rollback-per-test isolation, plus the *committed* concurrency test for reservation through the contract.
- E2E: the two required workflows via `WebApplicationFactory`.
- `appsettings.Testing.json` (or env/secret) with a **separate** Testing connection string; per-module
  `Sql/` folders + a simple runner (or DbUp).

**NOT now:** Docker/containers (excluded by brief); load/performance testing; mutation testing.

## Common mistakes to avoid

- **Tests pointing at the Development database.** A test run must never touch dev data.
- **Testing business rules through the database.** Slow, and a design smell.
- **Using rollback isolation for the concurrency test.** It must commit and clean up.
- **Faking the atomic proc in the concurrency test.** The whole point is the *real* database guard — fakes
  can't have race conditions.
- **Flaky e2e from shared state.** Reset the test DB via the fixture.

---

## ✅ Check Yourself

1. **What can an integration test catch that a unit test structurally cannot — and vice versa?**
2. **Why does "needing a database to test a business rule" indicate a design problem?**
3. **Why must the reservation-race test *commit* rather than roll back, unlike most integration tests?**
4. **What replaces ORM migrations here, and why does the same artifact serve both tests and deploys?**

---

## 👨‍🏫 Answer Key

**1. Integration vs. unit — what each catches.**
An **integration test** (real DB) catches things that exist only when real pieces connect: broken SQL,
Dapper mapping mistakes (including ID type handlers), transactions not committing/rolling back, concurrency
guards, and the cross-module contract wired correctly. A **unit test** catches wrong *business logic*
instantly with no database, and can isolate rules an integration test would drown in setup for. Each sees
what the other is blind to.

**2. Why is DB-needed-to-test-a-rule a design problem?**
A **business rule should live in the domain**, which depends on nothing and runs in milliseconds. If you
can't test a rule without a database, it has leaked into the database or a handler. The fix is to *move the
rule back into the domain* — the test difficulty is a symptom of a design smell.

**3. Why must the race test commit?**
Most integration tests wrap each test in a transaction and **roll back** to stay isolated. But the race test
checks what happens when two callers *commit* simultaneously — real committed concurrency, which you can't
observe from inside a transaction you're about to undo. So it must **commit for real** and clean up manually.

**4. What replaces migrations, and why one artifact for tests + deploys?**
**Ordered SQL scripts + a runner** (DbUp does this). The *same* scripts the test fixture applies are the
ones you run to deploy — one source of truth, so "the database my tests ran against" and "the database I
deployed" are built identically, with no drift.

> ✅ **You're ready for Phase 9 if:** you can match each bug type to the test level that catches it, and you
> understand why the concurrency test is special. Bonus: if your committed race test passes (exactly one
> reservation wins), you've *proven* Phase 5 works across the module wall — the most satisfying green test in
> the suite.
