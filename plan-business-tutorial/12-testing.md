# Phase 12 — Testing the Business Rules

> 📎 Slow version of "what each test level is *for*":
> [beginner Phase 8](../plan-beginner-friendly/09-phase-8-testing.md), including the line-by-line read of
> an xUnit test. Here we test the *real* Sales rules.

---

## 1. What we're building

Tests at the three levels, aimed at the business rules we built:

- **Unit tests** (no database) — the domain rules: blocking, credit limit, category role/quantity,
  status transitions, approval threshold.
- **Integration tests** (real test database) — the SQL that unit tests *can't* verify: the atomic
  reservation under concurrency, optimistic concurrency, transaction rollback.
- **End-to-end tests** (whole API) — the critical workflows: create → submit → approve, and create →
  submit → cancel (stock released).

## 2. Why it's designed this way

The architecture was *shaped* so the business rules are unit-testable without a database — this phase
collects that dividend. The guiding principle from
[beginner Phase 8](../plan-beginner-friendly/09-phase-8-testing.md): **if you need a database to test a
business rule, the rule is in the wrong place.** Almost all our rules are pure domain methods, so almost
all our rule tests are millisecond-fast unit tests. Only the genuinely DB-dependent things (atomic
reservation, concurrency, rollback) need integration tests — and those are exactly the bugs unit tests
*cannot* catch.

## 3. Unit tests — the business rules (fast, the bulk)

`Erp.Modules.Sales.UnitTests`. No mocks, no database — just objects.

```csharp
public class CustomerRuleTests
{
    [Fact]
    public void Blocked_customer_cannot_order()
    {
        var c = new Customer(new CustomerId(3), "Gamma", isBlocked: true, new Money(20_000), Money.Zero);
        var act = () => c.EnsureNotBlocked();
        act.Should().Throw<DomainException>().WithMessage("*blocked*");
    }

    [Fact]
    public void Order_over_available_credit_is_rejected()
    {
        // Beta: limit 5000, already owes 4500 → only 500 available.
        var c = new Customer(new CustomerId(2), "Beta", false, new Money(5000), new Money(4500));
        var act = () => c.EnsureWithinCreditLimit(new Money(600));   // 4500 + 600 = 5100 > 5000
        act.Should().Throw<DomainException>().WithMessage("*credit limit*");
    }

    [Fact]
    public void Order_within_available_credit_is_allowed()
    {
        var c = new Customer(new CustomerId(2), "Beta", false, new Money(5000), new Money(4500));
        var act = () => c.EnsureWithinCreditLimit(new Money(400));   // 4900 <= 5000
        act.Should().NotThrow();
    }
}

public class CategoryRuleTests
{
    [Fact]
    public void Controlled_category_requires_manager_role()
    {
        var controlled = new ItemCategory(2, "Controlled", maxQuantityPerOrder: 5, requiredRole: "SalesManager");
        controlled.Invoking(c => c.EnsureRoleAllowed("SalesRep"))
                  .Should().Throw<DomainException>();
        controlled.Invoking(c => c.EnsureRoleAllowed("SalesManager"))
                  .Should().NotThrow();
    }

    [Fact]
    public void Category_quantity_cap_is_enforced()
    {
        var controlled = new ItemCategory(2, "Controlled", maxQuantityPerOrder: 5, requiredRole: "SalesManager");
        controlled.Invoking(c => c.EnsureQuantityAllowed(6)).Should().Throw<DomainException>();
        controlled.Invoking(c => c.EnsureQuantityAllowed(5)).Should().NotThrow();
    }
}

public class SalesOrderStateTests
{
    [Fact]
    public void Cannot_approve_a_draft_order()
    {
        var order = SalesOrder.Create("SO-1", new CustomerId(1), new UserId(1));
        order.AddLine(new ItemId(1), new Quantity(1), new Money(100));
        // Draft → Approved is not a legal transition.
        order.Invoking(o => o.Approve(new UserId(9))).Should().Throw<DomainException>();
    }

    [Fact]
    public void Cannot_edit_lines_after_submit()
    {
        var order = SalesOrder.Create("SO-1", new CustomerId(1), new UserId(1));
        order.AddLine(new ItemId(1), new Quantity(1), new Money(100));
        order.Submit();
        order.Invoking(o => o.AddLine(new ItemId(2), new Quantity(1), new Money(50)))
             .Should().Throw<DomainException>();
    }

    [Fact]
    public void Order_over_threshold_is_flagged_high_value()   // tests the FLAG, not a workflow branch
    {
        var order = SalesOrder.Create("SO-1", new CustomerId(1), new UserId(1));
        order.AddLine(new ItemId(1), new Quantity(1), new Money(12_000));  // > $10k threshold
        order.RequiresApproval.Should().BeTrue();   // surfaced for a manager UI; see Phase 5 §8 ⚠️ note
    }
}
```

Every one of these runs in milliseconds and needs *nothing* to be set up. This is the payoff of putting
the rules in the domain. **These should be the bulk of your tests.**

> ❓ **You might be wondering: "Can I test the *handler* without a database?"** Yes — inject *fake*
> repositories. Because the handler depends on interfaces (`ICustomerRepository`, etc.), a test can pass
> in-memory fakes and verify orchestration (e.g. "a blocked customer never reaches the save step").
> That's an Application-layer unit test, and it works precisely because of the dependency rule. Handy,
> but the domain unit tests above give you the most value per line.

## 4. Integration tests — what unit tests can't catch

`Erp.Integration.Tests`, against a **separate test database** (never your dev DB). A fixture applies each
module's `Infrastructure/Sql/` scripts before the run — the same scripts from
[Phase 3](03-database-schema.md), which is why they double as your migration story.

```csharp
public sealed class TestDatabaseFixture : IAsyncLifetime
{
    public string ConnectionString => "Server=(localdb)\\MSSQLLocalDB;Database=Erp_Test;Trusted_Connection=True;TrustServerCertificate=True";

    public async Task InitializeAsync()
    {
        // create the test DB, then run each module's Sql/ scripts (schemas → tables → procs → seed) in order
    }
    public Task DisposeAsync() => Task.CompletedTask;
}
```

### The most important integration test in the whole suite: the reservation race

This proves [Phase 7](07-submit-reserve-concurrency.md)'s atomic guard actually works — the bug unit
tests *structurally cannot* catch.

```csharp
public class InventoryReservationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fx;
    public InventoryReservationTests(TestDatabaseFixture fx) => _fx = fx;

    [Fact]
    public async Task Two_concurrent_reservations_for_the_last_units_only_one_wins()
    {
        // Arrange: item 3 has OnHand=8, Reserved=0 (from seed). Two callers each want 5.
        var service = new InventoryService();

        // Act: fire both reservations at (nearly) the same time. NOTE: these must COMMIT
        // (real concurrency), not roll back — see the caveat below.
        var a = ReserveInOwnTransaction(itemId: 3, qty: 5);
        var b = ReserveInOwnTransaction(itemId: 3, qty: 5);
        bool[] results = await Task.WhenAll(a, b);

        // Assert: exactly one succeeded; the other saw insufficient stock.
        results.Count(r => r).Should().Be(1);

        // And the invariant held: Reserved never exceeded OnHand.
        var reserved = await GetReserved(itemId: 3);
        reserved.Should().Be(5);   // one winner reserved 5; 3 remain available
    }
}
```

> ⚠️ **Caveat (from [beginner Phase 8 §3](../plan-beginner-friendly/09-phase-8-testing.md)):** most
> integration tests wrap each test in a transaction they *roll back* for isolation. But this one must
> **commit**, because you can't observe committed-concurrency behavior from inside a transaction you're
> about to undo. Commit, assert, then clean up the reservation manually.

### Other integration tests worth writing

- **Transaction rollback:** submit an order where the *last* line is out of stock; assert the whole
  submit rolled back — the order stays Draft and *no* reservation exists for the earlier lines.
- **Optimistic concurrency:** load an order twice, update it once (bumping `RowVersion`), then update the
  stale copy; assert a `ConcurrencyException` (rowcount 0).
- **Round-trip mapping:** insert an order via the repo, load it back; assert the strongly-typed IDs,
  `Money`, and status survived the trip (proves the Dapper `TypeHandler`s work).
- **Balance is reversed on cancel-after-approve:** approve an order (customer balance goes up by the
  total), then cancel it; assert `OutstandingBalance` returns to its original value *and* the reservation
  was released. This guards the matched debit/credit pair from [Phase 8](08-approval-cancellation.md) — a
  regression here silently eats a customer's credit limit, so it's worth an explicit test:

```csharp
[Fact]
public async Task Cancelling_an_approved_order_restores_the_customer_balance()
{
    // Arrange: Acme (customer 1) starts at OutstandingBalance = 10,000 (from seed).
    var before = await GetOutstandingBalance(customerId: 1);

    // Act: create → submit → approve (balance += total) → cancel (balance -= total, stock released).
    var id = await CreateSubmitApprove(customerId: 1, itemId: 1, qty: 2);   // 2 × $25 = $50
    await Cancel(id);

    // Assert: balance is back where it started, and nothing is left reserved for those lines.
    (await GetOutstandingBalance(customerId: 1)).Should().Be(before);
    (await GetReserved(itemId: 1)).Should().Be(0);
}
```

## 5. End-to-end tests — the critical workflows

`WebApplicationFactory<Program>` boots the whole API in memory against the test DB and drives it over
HTTP. (Recall the `public partial class Program {}` we added in [Phase 1](01-solution-setup.md) — this
is why.)

```csharp
public class SalesOrderWorkflowTests : IClassFixture<CustomWebAppFactory>
{
    [Fact]
    public async Task Create_submit_approve_happy_path()
    {
        var client = _factory.CreateClient();

        // 1) Log in as a manager to get a token.
        var login = await client.PostAsJsonAsync("/auth/login", new { username = "manager", password = "Password123!" });
        var token = (await login.Content.ReadFromJsonAsync<LoginResult>())!.Token;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        // 2) Create a draft for Acme (healthy credit).
        var create = await client.PostAsJsonAsync("/sales-orders",
            new { customerId = 1, lines = new[] { new { itemId = 1, quantity = 2 } } });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await create.Content.ReadFromJsonAsync<CreatedResponse>())!.Id;

        // 3) Submit → reserves inventory.
        (await client.PostAsync($"/sales-orders/{id}/submit", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 4) Approve (manager) → Approved.
        (await client.PostAsync($"/sales-orders/{id}/approve", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 5) Read back → status Approved.
        var dto = await client.GetFromJsonAsync<SalesOrderDetailDto>($"/sales-orders/{id}");
        dto!.Status.Should().Be("Approved");
    }

    [Fact]
    public async Task SalesRep_cannot_approve()
    {
        var client = _factory.CreateClient();
        // log in as 'rep' (SalesRep) ... create + submit an order ...
        var approve = await client.PostAsync($"/sales-orders/{id}/approve", null);
        approve.StatusCode.Should().Be(HttpStatusCode.Forbidden);   // 403 — role gate works end-to-end
    }
}
```

The second test is gold: it proves the **role-based authorization** works through the *real* HTTP
pipeline — a SalesRep genuinely cannot approve, getting a 403 before the handler runs.

## 6. What to test where (the decision rule)

- **A business rule** (blocking, credit, category, transitions) → **unit test on the domain.** If you
  can't test it without a DB, move it into the domain.
- **SQL / mapping / transactions / concurrency** → **integration test.** These can't be faked.
- **"Do the parts connect, and does auth work?"** → **one e2e per critical workflow.** Not every
  permutation.

## 7. Why each test level exists

| Level | Catches (that others can't) |
|---|---|
| Unit | Wrong business logic; runs in ms; the bulk of tests. |
| Integration | Broken SQL, mapping errors, non-atomic reservation, failed rollback, stale-update detection. |
| E2E | Wiring/composition failures; auth actually enforced; the workflow fits together. |

## 8. Common mistakes & best practices

- ❌ **Tests pointing at the dev database.** ✅ A separate `Erp_Test` DB; a test run must never touch dev
  data.
- ❌ **Testing business rules through the API/DB.** ✅ Test them as domain unit tests — fast and a design
  check.
- ❌ **Rolling back the concurrency test.** ✅ It must commit to exercise real concurrency.
- ❌ **Only happy-path e2e.** ✅ Include a *forbidden* case (SalesRep approve → 403) — negative auth
  tests catch real holes.
- ✅ **Best practice: the pyramid.** Many unit tests, fewer integration, a handful of e2e. If you're
  writing lots of slow tests to cover business rules, your rules probably leaked out of the domain.

---

## ✅ Check Yourself

1. Why can almost all the business rules be tested without a database — and what does that tell you about
   the design?
2. Which single test proves the concurrency guard, and why must it commit rather than roll back?
3. Why is a "SalesRep gets 403 on approve" e2e test valuable?
4. What replaces EF migrations for setting up the test database?

> 👉 Next: [Phase 13 — Wrap-up & production-readiness checklist](13-wrap-up-checklist.md).
