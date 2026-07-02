# Phase 8 — Testing (unit, integration, end-to-end)

**Goal:** understand what each test level is *for*, and build all three against the real design. The
architecture was shaped so the domain is unit-testable without a DB and handlers are integration-
testable against real stored procedures — this phase collects that dividend.
**Produces:** domain/validator unit tests, handler+proc integration tests against a real SQL Server test
DB, and two end-to-end workflow tests. Separate Development/Testing connection strings. No Docker.
**Prereq:** Phases 3–7. The system works end-to-end manually.

---

## Step 1 — Why three levels (and what each is *for*)

| Level | Tests | Touches DB? | Catches | Speed |
|---|---|---|---|---|
| **Unit** | Domain rules, validators | No | Wrong business logic; invalid-state bugs | ms |
| **Integration** | Handler + repository + stored proc + transaction | **Yes** (real SQL Server) | Broken SQL, mapping errors, bad transaction/concurrency behavior | slow-ish |
| **End-to-end** | A whole workflow through the API | Yes | Wiring/composition errors; the parts don't fit | slowest |

The pyramid shape matters: many fast unit tests, fewer integration tests, a handful of e2e. If you
find yourself needing a database to test a *business rule*, the rule is in the wrong place — that's a
design smell this test structure surfaces.

## Step 2 — Unit tests (domain + validators, no DB)

This is the payoff of the rich domain. No mocks of a database, no fixtures — just objects.

```csharp
[Fact]
public void Booking_a_non_draft_order_throws()
{
    var order = Order.Create(new CustomerId(1));
    order.AddLine(new MaterialId(1), new Quantity(1), new Money(10m));
    order.Book(approvalGranted: true);                 // Draft -> Booked
    var act = () => order.Book(approvalGranted: true);  // Booked -> Booked illegal
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

Also unit-test validators (well-formedness) and value objects (`Money`/`Quantity` reject invalid
values). These run in milliseconds and should be the bulk of your tests.

## Step 3 — Integration tests (real SQL Server, separate DB)

Per your brief: **no Docker**, real SQL Server, separate connection strings. Structure:

- **`appsettings.Development.json`** → Development DB (your manual runs).
- **`appsettings.Testing.json`** (or user-secrets/env var `ConnectionStrings__Testing`) → a dedicated
  **test** database. The test project reads the Testing string; never point tests at Development.

**Schema management:** keep schema + procs as ordered `.sql` scripts in `db/`. A test fixture applies
them to the test DB before the run. This doubles as your migration story (§6).

**Isolation between tests:** each test runs in a transaction that is **rolled back** at teardown, so
tests don't pollute each other and the DB returns to a known state. (Alternative: truncate/reseed
between tests — slower but simpler to reason about. Rollback-per-test is the common choice; note the
caveat that it can't test *committed* concurrency behavior — for those specific tests, commit and clean
up explicitly.)

```csharp
public sealed class BookOrderTests : IClassFixture<TestDatabaseFixture>
{
    [Fact]
    public async Task Reserving_more_than_available_fails_atomically()
    {
        // Arrange: seed inventory OnHand=5, Reserved=0 for a material.
        // Act: two concurrent reservations of 5 (this test must COMMIT, not rollback,
        //      to exercise real concurrency — see isolation caveat above).
        // Assert: exactly one succeeds; OnHand-Reserved never goes negative.
    }
}
```

That concurrency test is the one that proves Phase 5's atomic SQL guard actually works — it's the
highest-value integration test in the suite, because it's the bug that unit tests *cannot* catch.

**What integration tests verify that unit tests can't:** stored procedures are correct SQL, the Dapper
mapping (including strongly-typed ID `TypeHandler`s) round-trips, transactions commit/rollback as
intended, and the optimistic-concurrency version check actually rejects stale updates.

## Step 4 — End-to-end tests (whole workflows)

Use `WebApplicationFactory<Program>` to spin up the API in-memory against the test DB, and drive it via
HTTP. Your two required workflows:

**Workflow 1 — create → reserve → book → verify:**
1. `POST /orders` (create draft) → capture id.
2. `POST /orders/{id}/book`.
3. Assert HTTP success; `GET /orders/{id}` shows `Booked`; query inventory shows the reservation;
   the DB row has the expected status and a bumped `rowversion`.

**Workflow 2 — create → delete/cancel → verify removal:**
1. `POST /orders` → id.
2. `POST /orders/{id}/cancel` (or a delete endpoint if you model hard deletes).
3. Assert the order is `Cancelled` (or gone), and any reservation was released.

E2E tests are few on purpose — they're slow and broad. Their job is to catch *composition* failures
(DI not wired, endpoint mis-mapped, a proc name typo) that lower levels miss because they test parts in
isolation.

## Step 5 — What to test where (the decision rule)

- A **business rule** → unit test on the domain. If you can't test it without a DB, move the rule into
  the domain.
- **SQL / mapping / transactions / concurrency** → integration test. These are exactly the things you
  *can't* fake meaningfully.
- **"Do the parts connect?"** → one e2e per critical workflow. Not every permutation — that's what the
  cheaper levels are for.

## Step 6 — Migrations without EF

No EF migrations here. Options, honestly compared:

- **Ordered SQL scripts + a runner** (recommended): `db/001_schema.sql`, `db/002_procs.sql`, … applied
  in order, tracked in a `SchemaVersions` table. Simple, transparent, versionable, and it's the same
  scripts your test fixture applies. Tools like DbUp (free) do exactly this if you don't want to
  hand-roll the runner.
- **SSDT / .dacpac**: declarative, diff-based deploys; heavier tooling, Windows/SQL-centric.
- **Manual scripts**: fine for a learning project but doesn't scale to a team.

Pick ordered-scripts-with-a-runner; it keeps schema, procs, tests, and deploys using one source of
truth and no ORM.

## What Sonnet should produce

- `Erp.Domain.Tests`: rules for booking/cancel/complete transitions, approval threshold, value objects.
- `Erp.Application.Tests`: validators; optionally handler tests with faked repositories for pure
  orchestration logic.
- `Erp.Integration.Tests`: `TestDatabaseFixture` (applies `db/` scripts to the Testing DB), rollback-
  per-test isolation, plus the committed concurrency test for reservation.
- E2E: the two required workflows via `WebApplicationFactory`.
- `appsettings.Testing.json` (or env/secret) with a **separate** Testing connection string; a `db/`
  script folder + a simple runner (or DbUp).

**NOT in this phase:** Docker/containers (excluded by brief); load/perf testing; mutation testing.

## Pitfalls

- **Tests pointing at the Development DB.** Enforce separate strings; a test run must never touch dev data.
- **Testing business rules through the database.** Slow and a design smell — the rule belongs in the
  domain where a unit test reaches it.
- **Rollback isolation for concurrency tests.** You can't observe committed-concurrency behavior inside
  a transaction you'll roll back — those specific tests must commit and clean up.
- **Flaky e2e from shared state.** Reset the test DB (fixture) so runs are deterministic.

---

## Checkpoint — you understand this phase when you can answer:

1. What can an integration test catch that a unit test structurally cannot, and vice versa?
2. Why does "needing a DB to test a business rule" indicate a design problem?
3. Why must the reservation-race test commit rather than roll back, unlike most integration tests?
4. What replaces EF migrations here, and why does the same artifact serve tests and deploys?
