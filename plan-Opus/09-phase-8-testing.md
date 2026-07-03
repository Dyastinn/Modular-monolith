# Phase 8 — Testing (unit, integration, end-to-end)

**Goal:** understand what each test level is *for*, and build all three. The architecture was shaped so the
domain is unit-testable without a DB and handlers are integration-testable against real procs — this phase
collects that dividend, including the cross-module submit→reserve path.
**Produces:** domain/validator unit tests, handler+proc integration tests against a real SQL Server test DB,
two e2e workflow tests. Separate Development/Testing connection strings. No Docker.
**Prereq:** Phases 3–7.

---

## Step 1 — Why three levels

| Level | Tests | Touches DB? | Catches | Speed |
|---|---|---|---|---|
| **Unit** | Domain rules, validators | No | Wrong business logic; invalid-state bugs | ms |
| **Integration** | Handler + repository + proc + transaction | **Yes** | Broken SQL, mapping, transaction/concurrency, the cross-module contract | slow-ish |
| **End-to-end** | A whole workflow through the API | Yes | Wiring/composition; parts don't fit | slowest |

Pyramid: many unit, fewer integration, a handful of e2e. Needing a DB to test a *business rule* is a design
smell.

## Step 2 — Unit tests (domain + validators, no DB) — `Erp.Modules.Sales.UnitTests`

```csharp
[Fact]
public void Submitting_a_non_draft_order_throws()
{
    var order = SalesOrder.Create(new CustomerId(1));
    order.AddLine(new ProductId(1), new Quantity(1), new Money(10m));
    order.Submit(approvalGranted: true);                 // Draft -> Submitted
    var act = () => order.Submit(approvalGranted: true);  // Submitted -> Submitted illegal
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

Also unit-test validators and value objects. These are the bulk of the suite.

## Step 3 — Integration tests (real SQL Server, separate DB)

No Docker; separate strings. `appsettings.Development.json` → dev DB; `appsettings.Testing.json` (or
`ConnectionStrings__Testing`) → a dedicated test DB — never point tests at Development.

**Schema:** each module's ordered `.sql` scripts live in its `Infrastructure/Sql/`; the fixture applies
*all* modules' scripts. **Isolation:** rollback-per-test — with the caveat that committed-concurrency tests
must commit and clean up.

```csharp
public sealed class SubmitSalesOrderTests : IClassFixture<TestDatabaseFixture>
{
    [Fact]
    public async Task Reserving_more_than_available_fails_atomically()
    {
        // Seed inventory.Stock OnHand=5, Reserved=0. Two concurrent reserves of 5 through IStockReservation
        // (must COMMIT). Assert exactly one wins; (OnHand-Reserved) never negative.
    }
}
```

That race test proves Phase 5's atomic guard *and* the cross-module boundary — the highest-value integration
test, and a bug unit tests can't catch. Integration tests also verify: procs are valid SQL, Dapper mapping
(incl. ID `TypeHandler`s) round-trips, transactions commit/rollback, the version check rejects stale updates.

## Step 4 — End-to-end (`WebApplicationFactory<Program>`)

**Workflow 1 — create → submit → verify (crosses the wall):** `POST /sales-orders` → `POST
/sales-orders/{id}/submit` → assert `Submitted`, `inventory.Stock` reserved, `rowversion` bumped.
**Workflow 2 — create → cancel → verify:** assert `Cancelled` and the reservation released via the contract.

E2E are few — they catch composition failures (DI, a module not registered, a proc-name typo).

## Step 5 — What to test where

Business rule → unit (domain). SQL/mapping/transactions/concurrency/contract → integration. "Parts connect?"
→ one e2e per critical workflow.

## Step 6 — Migrations without EF

Ordered SQL scripts + a runner (DbUp) — the same scripts the fixture applies and you deploy; tracked in
`SchemaVersions`. (SSDT/.dacpac heavier; manual doesn't scale.)

## What to produce

- `Erp.Modules.Sales.UnitTests`: transitions, approval threshold, value objects, validators (+ optional
  handler tests with faked repos and a fake `IStockReservation`).
- `Erp.Integration.Tests`: `TestDatabaseFixture` (applies each module's `Sql/`), rollback-per-test, the
  committed reservation-race test through the contract.
- E2E: the two workflows.
- `appsettings.Testing.json`; per-module `Sql/` + runner (or DbUp).

**NOT:** Docker; load/perf; mutation testing.

## Pitfalls

- Tests pointing at the Development DB.
- Testing business rules through the DB.
- Rollback isolation for the concurrency test (must commit).
- Faking the atomic proc in the race test (fakes can't race).
- Flaky e2e from shared state.

---

## Checkpoint

1. What can an integration test catch that a unit test can't, and vice versa?
2. Why is DB-needed-to-test-a-rule a design problem?
3. Why must the race test commit?
4. What replaces EF migrations, and why one artifact for tests + deploys?
