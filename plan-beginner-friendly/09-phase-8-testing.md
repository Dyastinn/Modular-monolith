# Phase 8 — Testing: Three Levels, Each With a Job

> 📄 Expert version: [`../plan-Opus/09-phase-8-testing.md`](../plan-Opus/09-phase-8-testing.md)

**In one sentence:** We write automated tests at three levels — fast rule-checks, real-database
checks, and whole-workflow checks — and learn what each level is *for* (and what it can't catch).

**What you'll build:** Domain/validator unit tests, handler+proc integration tests against a real SQL
Server test database, and two end-to-end workflow tests. Separate Development/Testing connection
strings. No Docker.

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

> 🔑 **A design signal hidden in here:** if you find you need a *database* to test a *business rule*,
> the rule is in the wrong place. It should be in the domain, where a millisecond-fast unit test can
> reach it. This test structure quietly tells you when your design has drifted.

## Step 2 — Unit tests (domain + validators, no database)

This is the payoff of the rich domain from Phase 3. No database mocks, no fixtures — just plain objects:

```csharp
[Fact]
public void Booking_a_non_draft_order_throws()
{
    var order = Order.Create(new CustomerId(1));
    order.AddLine(new MaterialId(1), new Quantity(1), new Money(10m));
    order.Book(approvalGranted: true);                 // Draft → Booked
    var act = () => order.Book(approvalGranted: true);  // Booked → Booked is illegal
    Assert.Throws<DomainException>(act);
}

[Fact]
public void Order_over_threshold_requires_approval()
{
    var order = Order.Create(new CustomerId(1));
    order.AddLine(new MaterialId(1), new Quantity(1), new Money(60_000m));
    Assert.Throws<DomainException>(() => order.Book(approvalGranted: false));
}
```

Also unit-test your validators (well-formedness) and value objects (`Money`/`Quantity` reject bad
values). These run in milliseconds and should be the **bulk** of your tests. *(Analogy: tasting the
sauce by itself.)*

## Step 3 — Integration tests (a real SQL Server, a separate database)

Per the brief: **no Docker**, a real SQL Server, and separate connection strings.

- **`appsettings.Development.json`** → your Development database (for manual runs).
- **`appsettings.Testing.json`** (or a user-secret / env var `ConnectionStrings__Testing`) → a
  dedicated **test** database. The test project reads the Testing string. **Never point tests at
  Development** — a test run must never touch your dev data.

**Managing the schema:** keep the schema and procs as ordered `.sql` scripts in a `db/` folder. A test
fixture applies them to the test database before the run. Bonus: this doubles as your "migrations"
story (Step 6).

**Keeping tests from polluting each other:** run each test inside a transaction that gets **rolled
back** at teardown, so the database returns to a known state. *(Analogy: set the table, eat, then reset
it exactly as it was.)*

```csharp
public sealed class BookOrderTests : IClassFixture<TestDatabaseFixture>
{
    [Fact]
    public async Task Reserving_more_than_available_fails_atomically()
    {
        // Arrange: seed inventory OnHand=5, Reserved=0 for a material.
        // Act: two CONCURRENT reservations of 5 (this test must COMMIT, not roll back,
        //      to exercise real concurrency — see the caveat below).
        // Assert: exactly one succeeds; (OnHand - Reserved) never goes negative.
    }
}
```

> 🌟 **That concurrency test is the single highest-value test in the whole suite.** It's the one that
> proves Phase 5's atomic SQL guard actually works — and it's a bug that unit tests *structurally
> cannot* catch, because unit tests have no concurrency.

> ⚠️ **Caveat:** the "roll back after each test" trick can't observe *committed* concurrency behavior
> (you're inside a transaction you're about to undo). So the reservation-race test specifically must
> **commit** and then clean up manually.

**What integration tests catch that unit tests can't:** that the stored procedures are valid SQL, that
Dapper's mapping (including the strongly-typed ID handlers) round-trips correctly, that transactions
commit/roll back as intended, and that the optimistic-concurrency version check really does reject
stale updates.

## Step 4 — End-to-end tests (whole workflows)

Use `WebApplicationFactory<Program>` to spin up the real API in-memory against the test database, and
drive it over HTTP. The two required workflows:

**Workflow 1 — create → book → verify:**
1. `POST /orders` (create a draft) → capture the id.
2. `POST /orders/{id}/book`.
3. Assert HTTP success; `GET /orders/{id}` shows `Booked`; inventory shows the reservation; the DB row
   has the expected status and a bumped `rowversion`.

**Workflow 2 — create → cancel → verify:**
1. `POST /orders` → id.
2. `POST /orders/{id}/cancel`.
3. Assert the order is `Cancelled` and any reservation was released.

E2E tests are few *on purpose* — they're slow and broad. Their job is to catch *composition* failures
(DI not wired, an endpoint mis-mapped, a proc name typo) that the lower levels miss because they test
pieces in isolation. *(Analogy: serving the whole meal to a guest and checking they're happy.)*

## Step 5 — The decision rule: what to test where

- A **business rule** → unit test on the domain. If you can't test it without a database, move the rule
  into the domain.
- **SQL / mapping / transactions / concurrency** → integration test. These are the things you *can't*
  fake meaningfully.
- **"Do the parts connect?"** → one e2e per critical workflow. Not every combination — that's what the
  cheaper levels are for.

## Step 6 — "Migrations" without an ORM

We have no EF migrations. The options, compared honestly:

- **Ordered SQL scripts + a runner (recommended):** `db/001_schema.sql`, `db/002_procs.sql`, … applied
  in order, tracked in a `SchemaVersions` table. Simple, transparent, versionable — and it's the *same*
  scripts your test fixture applies. A free tool called **DbUp** does exactly this if you don't want to
  write the runner yourself.
- **SSDT / .dacpac:** declarative, diff-based deploys; heavier tooling.
- **Manual scripts:** fine for learning, doesn't scale to a team.

Pick ordered-scripts-with-a-runner: one source of truth for schema, procs, tests, *and* deploys — no
ORM required.

## What to produce in this phase

- `Erp.Domain.Tests`: booking/cancel/complete transitions, the approval threshold, value objects.
- `Erp.Application.Tests`: validators; optionally handler tests with faked repositories.
- `Erp.Integration.Tests`: a `TestDatabaseFixture` (applies `db/` scripts to the Testing DB),
  rollback-per-test isolation, plus the *committed* concurrency test for reservation.
- E2E: the two required workflows via `WebApplicationFactory`.
- `appsettings.Testing.json` (or env/secret) with a **separate** Testing connection string; a `db/`
  script folder + a simple runner (or DbUp).

**NOT now:** Docker/containers (excluded by brief); load/performance testing; mutation testing.

## Common mistakes to avoid

- **Tests pointing at the Development database.** Enforce separate connection strings — a test run must
  never touch dev data.
- **Testing business rules through the database.** Slow, and a design smell — the rule belongs in the
  domain where a unit test reaches it.
- **Using rollback isolation for the concurrency test.** You can't observe committed-concurrency
  behavior inside a transaction you're going to undo — that test must commit and clean up.
- **Flaky e2e from shared state.** Reset the test DB (via the fixture) so runs are deterministic.

---

## ✅ Check Yourself

1. **What can an integration test catch that a unit test structurally cannot — and vice versa?**
2. **Why does "needing a database to test a business rule" indicate a design problem?**
3. **Why must the reservation-race test *commit* rather than roll back, unlike most integration
   tests?**
4. **What replaces ORM migrations here, and why does the same artifact serve both tests and deploys?**
