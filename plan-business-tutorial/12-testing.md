# Phase 12 — Testing the Business Rules

> Self-contained. You already started unit-testing the domain back in [Phase 5](05-domain-model.md) (that's
> where the `Erp.Modules.Sales.UnitTests` project was created) — this phase **rounds out** the suite and adds
> the two harder levels: integration and end-to-end. Money is in **Philippine Pesos (₱)**.
>
> We explain what each test level is *for* before writing any of them — the levels are not "the same test,
> bigger," they answer different questions.

---

> ⏱️ **Time for this phase:** ~2 hr. The unit tests fly (pure domain); the integration/e2e setup — a test
> database and the concurrency test — takes the most of it. Worth every minute: this is your safety net.

## 1. What we're building

Tests at the three levels, aimed at the business rules we built:

- **Unit tests** (no database) — the domain rules: blocking, credit limit, category role/quantity,
  status transitions, approval threshold.
- **Integration tests** (real test database) — the SQL that unit tests *can't* verify: the atomic
  reservation under concurrency, optimistic concurrency, transaction rollback.
- **End-to-end tests** (whole API) — the critical workflows: create → submit → approve, create → submit →
  cancel (stock released), and login → refresh → replay (reuse detection revokes the session).

## 2. Why it's designed this way

The architecture was *shaped* so the business rules are unit-testable without a database — this phase
collects that dividend. The guiding principle, and it's worth memorizing: **if you need a database to test a
business rule, the rule is in the wrong place.** Almost all our rules are pure domain methods, so almost
all our rule tests are millisecond-fast unit tests. Only the genuinely DB-dependent things (atomic
reservation, concurrency, rollback) need integration tests — and those are exactly the bugs unit tests
*cannot* catch.

## 3. Unit tests — the business rules (fast, the bulk)

`Erp.Modules.Sales.UnitTests` (the project you created in [Phase 5](05-domain-model.md)). No mocks, no
database — just objects. Add these alongside the first tests you wrote there:

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
        order.AddLine(new ItemId(1), new Quantity(1), new Money(60_000));  // > ₱50,000 threshold
        order.RequiresApproval.Should().BeTrue();   // surfaced for a manager UI; see Phase 5 §8 ⚠️ note
    }
}
```

Every one of these runs in milliseconds and needs *nothing* to be set up. This is the payoff of putting
the rules in the domain. **These should be the bulk of your tests.**

> ❓ **You might be wondering: "Can I test the *handler* without a database?"** Yes — inject *fake*
> repositories. Because the handler depends on interfaces (`ICustomerRepository`, etc.), a test can pass
> in-memory fakes and verify orchestration (e.g. "a blocked customer never reaches the save step").
> That's a *handler* unit test, and it works precisely because of the port/adapter split from
> [Phase 6 §4](06-create-order.md). Handy — but the domain unit tests above give you the most value per line,
> because they test the *rules*, and the handler has none.

## 4. Integration tests — what unit tests can't catch

> 📦 **Create the integration project now (this is the first phase that needs it):**
> ```bash
> dotnet new xunit -n Erp.Integration.Tests -o tests/Erp.Integration.Tests
> dotnet sln add tests/Erp.Integration.Tests
> dotnet add tests/Erp.Integration.Tests reference src/Erp.Api/Erp.Api.csproj
> dotnet add tests/Erp.Integration.Tests package FluentAssertions
> dotnet add tests/Erp.Integration.Tests package Microsoft.AspNetCore.Mvc.Testing   # WebApplicationFactory (§5)
> dotnet add tests/Erp.Integration.Tests package Microsoft.Data.SqlClient
> ```
> (Referencing `Erp.Api` transitively brings in the modules, so the tests can call the real
> `StockReservationService` and boot the real host. CPM adds the `<PackageVersion>` lines.)

`Erp.Integration.Tests` runs against a **separate test database**, `Erp_Test` — never your dev DB. Setting it
up costs you nothing extra: the app already applies each module's `Infrastructure/Sql/` scripts on startup
([Phase 3](03-database-schema.md)), so booting the host against a different connection string is all it takes.
Point it at `Erp_Test` and the same schemas, procs, and seed data appear there. **This is the payoff of
"migrations without EF"** — one set of scripts builds dev, test, and production alike.

That means there's no separate database fixture to write. The `CustomWebAppFactory` in §5 does the whole job,
and the integration tests below reach the database through the *same* connection string it configures:

```csharp
public const string TestConnectionString =
    "Server=(localdb)\\MSSQLLocalDB;Database=Erp_Test;Trusted_Connection=True;TrustServerCertificate=True";
```

> ⚠️ **A stale `Erp_Test` will lie to you.** Because the scripts are applied on boot and are guarded to be
> re-runnable, a test database created by an *older* version of your schema doesn't get rebuilt — it gets
> patched, and only where the scripts say so. Seed rows in particular are inserted only `IF NOT EXISTS`. When
> a test starts failing for reasons that make no sense (a customer with the wrong balance, an item that isn't
> there), **drop `Erp_Test` and let it rebuild.** It's the first thing to try, and it's usually the answer.

### The most important integration test in the whole suite: the reservation race

This proves [Phase 7](07-submit-reserve-concurrency.md)'s atomic guard actually works — the bug unit
tests *structurally cannot* catch.

```csharp
// tests/Erp.Integration.Tests/InventoryReservationTests.cs
[Collection("Integration")]   // shares the factory, so Erp_Test exists and no other class runs concurrently
public class InventoryReservationTests
{
    private readonly CustomWebAppFactory _factory;
    public InventoryReservationTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Two_concurrent_reservations_for_the_last_units_only_one_wins()
    {
        // Arrange: item 3 has OnHand=8, Reserved=0 (from seed). Two callers each want 5.

        // Act: fire both reservations at (nearly) the same time. NOTE: each opens and COMMITS its own
        // transaction (real concurrency), NOT rolls back — see the caveat below.
        var a = ReserveInOwnCommittedTransaction(itemId: 3, qty: 5);
        var b = ReserveInOwnCommittedTransaction(itemId: 3, qty: 5);
        bool[] results = await Task.WhenAll(a, b);

        // Assert: exactly one succeeded; the other saw insufficient stock.
        results.Count(r => r).Should().Be(1);

        // And the invariant held: Reserved never exceeded OnHand.
        (await GetReserved(itemId: 3)).Should().Be(5);   // one winner reserved 5; 3 remain available

        await ResetReservation(itemId: 3);   // manual cleanup — we committed, so there's nothing to roll back
    }

    // --- test helpers: each opens a real connection + a COMMITTED transaction against the test DB ---
    private async Task<bool> ReserveInOwnCommittedTransaction(long itemId, int qty)
    {
        await using var conn = new SqlConnection(CustomWebAppFactory.TestConnectionString);
        await conn.OpenAsync();
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
        var service = new StockReservationService();          // the REAL Inventory implementation (Phase 7)
        var reserved = await service.TryReserveAsync(itemId, qty, tx, default);
        await tx.CommitAsync();                                // COMMIT — so the race is actually observable
        return reserved;
    }

    // Bodies elided for brevity — each is a one-line Dapper query on a fresh connection:
    private Task<int> GetReserved(long itemId) => /* SELECT Reserved FROM inventory.Inventory WHERE ItemId=@itemId */ ;
    private Task ResetReservation(long itemId) => /* UPDATE inventory.Inventory SET Reserved = 0 WHERE ItemId=@itemId */ ;
}
```

> 🧪 **Note:** `ReserveInOwnCommittedTransaction`, `GetReserved`, and `ResetReservation` are little **test
> helpers** you write once in this class (their bodies are trivial Dapper calls on the fixture's connection
> string). The star is `new StockReservationService()` — the *actual* Inventory implementation from
> [Phase 7](07-submit-reserve-concurrency.md), calling the *actual* atomic proc. We use the real thing on
> purpose: **a fake can't have a race condition, so a fake would prove nothing here.**

> ⚠️ **Caveat, and it catches people.** Most integration tests wrap each test in a transaction they *roll
> back* afterwards, which keeps tests isolated from each other. But this one must
> **commit**, because you can't observe committed-concurrency behavior from inside a transaction you're
> about to undo. Commit, assert, then clean up the reservation manually.

### Other integration tests worth writing

- **Transaction rollback:** submit an order where the *last* line is out of stock; assert the whole
  submit rolled back — the order stays Draft and *no* reservation exists for the earlier lines.
- **Optimistic concurrency:** load an order twice, update it once (bumping `RowVersion`), then update the
  stale copy; assert a `ConcurrencyException` (rowcount 0).
- **Round-trip mapping:** insert an order via the repo, load it back; assert the strongly-typed IDs,
  `Money`, and status survived the trip (proves the repository's unwrap-and-rewrap at the database boundary
  is symmetric — `order.CustomerId.Value` out, `new CustomerId(...)` back in).
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
    var id = await CreateSubmitApprove(customerId: 1, itemId: 1, qty: 2);   // 2 × ₱25 = ₱50
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

`CustomWebAppFactory` is a small subclass of `WebApplicationFactory<Program>`. It has **three** jobs, and
each one corresponds to a trap that will otherwise cost you an afternoon:

```csharp
// tests/Erp.Integration.Tests/CustomWebAppFactory.cs
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Erp.Integration.Tests;

public sealed class CustomWebAppFactory : WebApplicationFactory<Program>
{
    public const string TestConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=Erp_Test;Trusted_Connection=True;TrustServerCertificate=True";

    public const string TestApiKey = "test-suite-key";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // (1) Development, so appsettings.Development.json loads — that's where Jwt:SigningKey lives.
        //     Without it the app boots with an empty signing key and every login throws.
        builder.UseEnvironment("Development");

        // (2) Point at the TEST database, never your dev one.
        builder.UseSetting("ConnectionStrings:ErpDb", TestConnectionString);

        // (3) Give the test suite its own API key, as if it were another client app.
        builder.UseSetting("ApiKeys:tests", TestApiKey);
    }

    // Every client this factory hands out already carries the API key.
    protected override void ConfigureClient(HttpClient client)
    {
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiKey);
        base.ConfigureClient(client);
    }
}
```

> ⚠️ **The trap that bites everyone: `X-Api-Key`.** The gate from [Phase 4c §11](04c-jwt-refresh-apikey.md)
> runs in front of *every* request, including `/auth/login`. Boot the app in a test, call `/auth/login`, and
> you get a `401` whose body says `"Missing or invalid API key."` — which looks exactly like a broken login.
>
> Notice how we fix it: the test suite gets **its own key**, registered like any other client, and
> `ConfigureClient` attaches it. We do *not* exempt tests from the middleware. A test that skips the gate
> isn't exercising the pipeline your users hit, and the one thing an integration test exists to do is exercise
> the real pipeline.

All the integration tests share one `Erp_Test` database, so they must not run **in parallel** against it — a
race between two test classes would look exactly like a flaky concurrency bug. xUnit serializes classes that
share a collection:

```csharp
// tests/Erp.Integration.Tests/IntegrationCollection.cs
[CollectionDefinition("Integration")]
public sealed class IntegrationCollection : ICollectionFixture<CustomWebAppFactory> { }
```

```csharp
// tests/Erp.Integration.Tests/SalesOrderWorkflowTests.cs
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Erp.Integration.Tests;

[Collection("Integration")]   // ← shares the one factory; classes run one at a time
public class SalesOrderWorkflowTests
{
    private readonly CustomWebAppFactory _factory;
    public SalesOrderWorkflowTests(CustomWebAppFactory factory) => _factory = factory;

    // Tiny local shapes for reading the JSON back. We deliberately do NOT reuse the API's own DTOs —
    // a test that shares types with the code it tests can't catch a contract change.
    private sealed record LoginResponse(string AccessToken, string RefreshToken, string Username, string Role);
    private sealed record CreatedResponse(long Id);
    private sealed record DetailResponse(string Status);

    // Log in and return a client carrying both the API key (from the factory) and the bearer token.
    private async Task<HttpClient> LoggedInAs(string username)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/auth/login", new { username, password = "Password123!" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        return client;
    }

    [Fact]
    public async Task Create_submit_approve_happy_path()
    {
        var client = await LoggedInAs("manager");

        // Create a draft for Acme (healthy credit).
        var create = await client.PostAsJsonAsync("/sales-orders",
            new { customerId = 1, lines = new[] { new { itemId = 1, quantity = 2 } } });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await create.Content.ReadFromJsonAsync<CreatedResponse>())!.Id;

        // Submit → reserves inventory.
        (await client.PostAsync($"/sales-orders/{id}/submit", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Approve (manager) → Approved.
        (await client.PostAsync($"/sales-orders/{id}/approve", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Read back → status Approved.
        var dto = await client.GetFromJsonAsync<DetailResponse>($"/sales-orders/{id}");
        dto!.Status.Should().Be("Approved");
    }

    [Fact]
    public async Task SalesRep_cannot_approve()
    {
        var manager = await LoggedInAs("manager");
        var id = await CreateAndSubmit(manager, customerId: 1, itemId: 1, qty: 1);

        var rep = await LoggedInAs("rep");
        var approve = await rep.PostAsync($"/sales-orders/{id}/approve", null);
        approve.StatusCode.Should().Be(HttpStatusCode.Forbidden);   // 403 — role gate works end-to-end
    }

    [Fact]
    public async Task Replaying_a_rotated_refresh_token_revokes_the_whole_session()
    {
        var client = _factory.CreateClient();   // no bearer token needed — the refresh token IS the credential
        var login = (await (await client.PostAsJsonAsync("/auth/login",
            new { username = "rep", password = "Password123!" })).Content.ReadFromJsonAsync<LoginResponse>())!;

        // Rotate once — the first refresh token is now spent, and we hold a fresh pair.
        var rotated = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = login.RefreshToken });
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);
        var fresh = (await rotated.Content.ReadFromJsonAsync<LoginResponse>())!;

        // Replay the OLD (spent) token → reuse detected → 401, and the entire family is revoked...
        (await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = login.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // ...so even the legitimately-rotated token is now dead — the breach burned the lineage.
        (await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = fresh.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

The second test is gold: it proves the **role-based authorization** works through the *real* HTTP
pipeline — a SalesRep genuinely cannot approve, getting a 403 before the handler runs. The third does the
same for **refresh-token reuse detection** ([Phase 4 §10](04c-jwt-refresh-apikey.md)): a replayed token doesn't just fail,
it revokes the whole family — the security property that's easy to *claim* and worth a test that *proves* it.

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

## 9. Checkpoint — the whole suite goes green ✅

Run everything:

```bash
dotnet test
```

You should see the fast domain **unit tests** (from Phase 5 + §3), the **integration tests** (the reservation
race, rollback, optimistic concurrency, balance-reversal — against `Erp_Test`), and the **e2e tests**
(create→submit→approve, the `403` role gate, refresh-token reuse detection) all pass. If the suite is green —
especially the concurrency test that *commits* — **Phase 12 is done and verifiable**: you have a safety net
that catches the bugs unit tests can't, and proof the architecture holds end to end.

---

## ✅ Check Yourself

1. Why can almost all the business rules be tested without a database — and what does that tell you about
   the design?
2. Which single test proves the concurrency guard, and why must it commit rather than roll back?
3. Why is a "SalesRep gets 403 on approve" e2e test valuable?
4. What replaces EF migrations for setting up the test database?

> 👉 Next: [Phase 13 — Wrap-up & production-readiness checklist](13-wrap-up-checklist.md).
