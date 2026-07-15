# Phase 6 — Create Sales Order (End to End, With Real Checks)

> Self-contained: every rule and every layer is explained here. Money is in **Philippine Pesos (₱)**.

---

> ⏱️ **Time for this phase:** ~2 hr. This is the **template slice** every later feature copies — endpoint →
> validator → handler → domain → repository → transaction. Invest here; the rest go faster because of it.

## 1. What we're building

The `POST /sales-orders` feature, all the way through: endpoint → validation → handler → domain →
repository → Dapper → stored procedure → transaction → response. When done, a logged-in salesperson can
create a **Draft** order, and the system enforces:

- Input is well-formed (validator).
- The customer exists and is **not blocked**.
- Each line's item exists and is active; its **category role restriction** is satisfied by the caller.
- Each category's **per-order quantity limit** is respected.
- The order total is **within the customer's credit limit**.
- The whole thing saves in a **transaction** (header + all lines, all-or-nothing).

This is the template every other command copies. Study it.

## 2. Why it's designed this way

Three ideas come together in this slice, and every later feature reuses the same three:

- **Two kinds of validation, and they are not the same job.** *Input validation* asks "is this request even
  well-formed — is `customerId` present, are the quantities positive?" and lives in the validator. *Business
  rules* ask "is this operation allowed — is the customer blocked, are they over their credit limit?" and
  live in the domain. Mixing them is the most common way this architecture rots: a business rule in a
  validator can't be unit-tested with the entity it's about, and it silently stops being enforced the moment
  some other slice writes the same data.
- **Handlers orchestrate, the domain decides.** The handler loads data and calls domain methods; it
  holds no policy `if`s.
- **Explicit transaction** so the boundary is visible while learning.

## 3. The command, validator, and DTO

The whole feature is **two files** in `Modules/Sales/Features/CreateSalesOrder/`:
`CreateSalesOrderHandler.cs` (command + result + validator + handler) and `CreateSalesOrderEndpoint.cs`
(request + response + Carter endpoint). Here's the top of `CreateSalesOrderHandler.cs`:

```csharp
// src/Modules/Sales/Features/CreateSalesOrder/CreateSalesOrderHandler.cs
using Erp.Modules.Sales.Domain;
using Erp.Shared.Common;        // NotFoundException, ICurrentUser
using Erp.Shared.Common.Data;   // IDbConnectionFactory
using Erp.Shared.Messaging;     // ICommand, ICommandHandler
using FluentValidation;

namespace Erp.Modules.Sales.Features;

public sealed record CreateSalesOrderLineDto(long ItemId, int Quantity);

public sealed record CreateSalesOrderCommand(
    long CustomerId,
    IReadOnlyList<CreateSalesOrderLineDto> Lines) : ICommand<long>;   // returns the new order id

public sealed class CreateSalesOrderValidator : AbstractValidator<CreateSalesOrderCommand>
{
    public CreateSalesOrderValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("An order needs at least one line.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ItemId).GreaterThan(0);
            line.RuleFor(l => l.Quantity).GreaterThan(0);
        });
    }
}
```

> 🔑 Notice the validator only checks **shape** — positive ids, positive quantities, at least one line.
> It does **not** check "is the customer blocked" or "over credit limit." Those need to load data and
> are *business rules* → they belong in the handler/domain. Mixing them here would scatter your rules and
> make the domain non-authoritative. This line-drawing is the single most common thing beginners get
> wrong; get it right here and every slice after is easy.

## 4. The repository interfaces (the module declares what it needs)

The `SalesOrder` repository interface belongs to the **module** (it's specific to this aggregate), and it
lives in `Modules/Sales/Domain`. The three repository interfaces are the same *kind* of thing — the
data-access "ports" this module declares — so they're grouped together under a `Ports/` folder, one file
each, rather than scattered among the entity folders. The generic data plumbing they use —
`IDbConnectionFactory` — comes from `Shared` because every module needs it. All three, shown together:

```csharp
// Domain/Ports/ISalesOrderRepository.cs — the SalesOrder aggregate's data-access port
using System.Data;   // IDbTransaction

namespace Erp.Modules.Sales.Domain;

public interface ISalesOrderRepository
{
    Task<string> NextOrderNumberAsync(CancellationToken ct);
    Task<long> AddAsync(SalesOrder order, IDbTransaction tx, CancellationToken ct);   // returns the DB-assigned id
    Task<SalesOrder?> GetAsync(SalesOrderId id, CancellationToken ct);
    Task UpdateStatusAsync(SalesOrder order, IDbTransaction tx, CancellationToken ct);
}
```

```csharp
// Domain/Ports/ICustomerRepository.cs — the Customer aggregate's data-access port
using System.Data;   // IDbTransaction

namespace Erp.Modules.Sales.Domain;

public interface ICustomerRepository
{
    Task<Customer?> GetAsync(CustomerId id, CancellationToken ct);
    // delta is a SIGNED decimal, not Money: approval adds the total (+), cancelling an approved order
    // reverses it (−). Money forbids negatives, so a balance *delta* can't be a Money. (Phase 8)
    Task AdjustBalanceAsync(CustomerId id, decimal delta, IDbTransaction tx, CancellationToken ct);
}
```

```csharp
// Domain/Ports/IItemRepository.cs — the ItemCategory aggregate's data-access port

namespace Erp.Modules.Sales.Domain;

public interface IItemRepository
{
    // Loads the requested items WITH their category rules, in one round-trip.
    Task<IReadOnlyList<ItemForOrder>> GetForOrderAsync(IReadOnlyList<long> itemIds, CancellationToken ct);
}

// A small read shape the handler needs: item + its price + its category rules.
public sealed record ItemForOrder(long ItemId, bool IsActive, Money UnitPrice, ItemCategory Category);
```

> ❓ **You might be wondering: "Why does the interface live in `Domain/` but the implementation in
> `Infrastructure/`?"** It's the dependency rule from [Phase 1](01-solution-setup.md), and it's the same
> port/adapter move you've now seen three times — `IDbConnectionFactory` in [Phase 3b](03b-data-access.md),
> `ICurrentUser` and `IUserRepository` in [Phase 4b](04b-jwt-login.md), and these repositories here. The
> domain needs *a way to load a customer*, so the domain **declares** the interface. The Dapper/SQL code
> **implements** it, one folder over. The domain never learns SQL exists.
>
> The rule of thumb: **whoever needs a capability owns the interface for it; whoever can talk to the outside
> world implements it.** Note this is *not* a Clean Architecture "Application layer" — there is no such
> project here. The port sits inside the module that needs it, right next to the aggregate it serves.

> 🔑 **These are aggregate-specific, never a generic `Repository<T>`.** `ISalesOrderRepository` speaks in
> `SalesOrder`s and hands back whole aggregates; `IItemRepository` returns a purpose-built read shape
> (`ItemForOrder`) that exists only because *this* handler needs an item's price and category together. A
> generic `Repository<T>` with `GetById`/`Add`/`Update` would force every caller into the same shape and
> quietly push the "load the category too" decision back up into the handler.

## 5. The handler — where the business rules come together

This is the meat. Read every line; the comments mark which layer owns each decision.

> 📦 **One tiny file first, or the handler won't compile.** The handler below throws `NotFoundException` when
> a customer or item isn't there. That type doesn't exist yet — create it now:
>
> ```csharp
> // src/Shared/Erp.Shared.Common/Exceptions.cs
> namespace Erp.Shared.Common;
>
> public class NotFoundException(string message) : Exception(message);   // "that row doesn't exist"
> ```
>
> ❓ **"Why not put it in `BuildingBlocks` next to `DomainException`?"** Because the two say different things.
> `DomainException` means *a business rule said no* — a `SalesOrder` throws it, and it belongs to the domain's
> vocabulary. `NotFoundException` means *the row you asked for isn't there*, which is a fact about a failed
> **request**, not about the business. Keeping it in `Common` means the pure domain never has to know that
> "not found" is even a category of problem. (Right now nothing catches either one, so a throw becomes an
> ugly 500. [Phase 11](11-cross-cutting.md) adds the one handler that maps `NotFoundException` → **404** and
> `DomainException` → **422**. We'll grow this file with one more exception as we hit the need for it.)

```csharp
public sealed class CreateSalesOrderHandler(
    ICustomerRepository customers,
    IItemRepository items,
    ISalesOrderRepository orders,
    IDbConnectionFactory factory,
    ICurrentUser currentUser) : ICommandHandler<CreateSalesOrderCommand, long>   // command marker (Phase 2)
{
    public async Task<long> Handle(CreateSalesOrderCommand cmd, CancellationToken ct)
    {
        // 1) Load the customer; must exist.
        var customer = await customers.GetAsync(new CustomerId(cmd.CustomerId), ct)
            ?? throw new NotFoundException($"Customer {cmd.CustomerId} not found.");

        // 2) BUSINESS RULE (domain): customer must not be blocked.
        customer.EnsureNotBlocked();

        // 3) Load all requested items + their category rules in one go.
        var itemIds = cmd.Lines.Select(l => l.ItemId).Distinct().ToList();
        var itemLookup = (await items.GetForOrderAsync(itemIds, ct)).ToDictionary(i => i.ItemId);

        // 4) Build the order THROUGH the domain.
        var orderNumber = await orders.NextOrderNumberAsync(ct);
        var order = SalesOrder.Create(orderNumber, customer.Id, new UserId(currentUser.UserId));

        // Track quantity per category to enforce the category cap across the whole order.
        var qtyByCategory = new Dictionary<long, int>();

        foreach (var line in cmd.Lines)
        {
            if (!itemLookup.TryGetValue(line.ItemId, out var item) || !item.IsActive)
                throw new NotFoundException($"Item {line.ItemId} not found or inactive.");

            // 5) BUSINESS RULE (domain): does the caller's role satisfy the category restriction?
            item.Category.EnsureRoleAllowed(currentUser.Role);

            // Accumulate category quantity, then check the cap.
            qtyByCategory.TryGetValue(item.Category.Id, out var running);
            running += line.Quantity;
            qtyByCategory[item.Category.Id] = running;

            // 6) BUSINESS RULE (domain): per-order category quantity limit.
            item.Category.EnsureQuantityAllowed(running);

            // 7) Add the line through the aggregate (enforces draft-only, positive qty).
            order.AddLine(new ItemId(line.ItemId), new Quantity(line.Quantity), item.UnitPrice);
        }

        // 8) BUSINESS RULE (domain): the total must fit the customer's credit limit.
        customer.EnsureWithinCreditLimit(order.Total);

        // 9) Persist in ONE transaction (header + all lines, all-or-nothing).
        using var conn = factory.Create();
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        try
        {
            var newId = await orders.AddAsync(order, tx, ct);   // AddAsync hands back the DB id
            tx.Commit();
            return newId;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
```

> ⚠️ **Return the id from `AddAsync` — don't read `order.Id`.** The database assigns the id on `INSERT`, but
> `SalesOrder.Id` has a `{ get; protected set; }` ([Phase 5](05-domain-model.md)), so the repository (not a
> subclass) can't write it back onto the aggregate. If you `return order.Id.Value` here you'd hand back `0`,
> because the in-memory order never learned its real id. So `AddAsync` **returns** the new id and the handler
> returns *that*.

### The division of labor (this is the whole lesson)

Look at what the handler *does* vs. what it *decides*:

- The handler **orchestrates**: load customer, load items, build order, save. It has loops and null
  checks — that's *coordination*, which is fine.
- The **domain decides every rule**: `EnsureNotBlocked`, `EnsureRoleAllowed`, `EnsureQuantityAllowed`,
  `EnsureWithinCreditLimit`, and `AddLine`'s draft check. There is **not one** business `if` in the
  handler — every policy call is `something.EnsureX()`.

> 🔑 **The tell:** if you ever find yourself writing `if (customer.IsBlocked) throw ...` in the handler,
> stop. That belongs as `customer.EnsureNotBlocked()` in the domain. The handler asking a question is
> fine; the handler *knowing the answer's logic* is the smell.

> ⚠️ **Scope note on the credit check.** `EnsureWithinCreditLimit(order.Total)` compares *this one order's*
> total against the customer's already-owed balance. It does **not** account for the customer's other
> *draft/pending* orders (their balance only changes at approval — [Phase 8](08-approval-cancellation.md)).
> That's a deliberate simplification, explained in full in the ⚠️ note in
> [Phase 5 §7](05-domain-model.md) and listed on the [Phase 13 checklist](13-wrap-up-checklist.md). For now
> just know the boundary: one order at a time, not the whole in-flight pipeline.

## 6. The Dapper repository (the module's Infrastructure)

> 📦 **Package for this phase — add this now.** This is the first phase that runs SQL from a *module*, so the
> Sales module gets its own Dapper reference:
> ```bash
> dotnet add src/Modules/Sales package Dapper
> ```
> (The shared data-access packages — `Dapper` and `Microsoft.Data.SqlClient` in `Shared.Infrastructure`, where
> the connection factory creates `SqlConnection`s — were added back in
> [Phase 3b](03b-data-access.md). The module just uses Dapper against the `DbConnection` the factory hands
> out. CPM adds the `<PackageVersion>` line.)

`Modules/Sales/Infrastructure/SalesOrderRepository.cs` (abbreviated to the create path):

```csharp
// src/Modules/Sales/Infrastructure/SalesOrderRepository.cs
using System.Data;              // CommandType, IDbTransaction
using Dapper;                   // ExecuteAsync, ExecuteScalarAsync, CommandDefinition
using Erp.Modules.Sales.Domain;
using Erp.Shared.Common.Data;   // IDbConnectionFactory

namespace Erp.Modules.Sales.Infrastructure;

public sealed class SalesOrderRepository(IDbConnectionFactory factory) : ISalesOrderRepository
{
    public async Task<long> AddAsync(SalesOrder order, IDbTransaction tx, CancellationToken ct)
    {
        var conn = tx.Connection!;

        // Insert the header, get the new id back.
        var newId = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "sales.usp_SalesOrder_Insert",
            new
            {
                order.OrderNumber,
                CustomerId = order.CustomerId.Value,
                Status = (int)order.Status,
                TotalAmount = order.Total.Amount,
                CreatedByUserId = order.CreatedBy.Value
            },
            transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));

        // Insert each line, tied to the new order id.
        foreach (var line in order.Lines)
            await conn.ExecuteAsync(new CommandDefinition(
                "sales.usp_SalesOrderLine_Insert",
                new
                {
                    SalesOrderId = newId,
                    ItemId = line.ItemId.Value,
                    Quantity = line.Quantity.Value,
                    UnitPrice = line.UnitPrice.Amount
                },
                transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));

        return newId;   // hand the DB-assigned id back to the handler
    }
    // GetAsync, UpdateStatusAsync, NextOrderNumberAsync omitted here — same shape.
}
```

The repository maps the aggregate to proc parameters and runs them **on the passed-in transaction**. It
contains **no business rules** — pure persistence. Note it converts domain types to primitives at the
boundary: `order.CustomerId.Value`, `(int)order.Status`, `line.UnitPrice.Amount`. That unwrapping is the
whole price of the strongly-typed IDs from [Phase 5](05-domain-model.md), and it's paid here, once.

### Register the three repositories

`AddSalesModule()` already discovers *handlers* and *validators* by scanning the assembly
([Phase 2](02-mediator-own-vs-free.md)) — but nothing tells the container which class implements
`ISalesOrderRepository`. Add those three lines now, or the very first request fails with *"No service for type
`ISalesOrderRepository` has been registered."*

```csharp
// src/Modules/Sales/DependencyInjection.cs, inside AddSalesModule()
services.AddScoped<ISalesOrderRepository, SalesOrderRepository>();
services.AddScoped<ICustomerRepository, CustomerRepository>();
services.AddScoped<IItemRepository, ItemRepository>();
```

> ❓ **Why not scan for these too?** We could — a reflection loop matching `IFoo` to `Foo` isn't hard. But
> repository registration is exactly the place you later want to say something *specific* (wrap one in a
> cache, swap one for a fake in a test host, register one as a singleton). Three explicit lines are a list
> you can read; a convention is a rule you have to reverse-engineer. Handlers are numerous and uniform, so
> they get the scan. Repositories are few and load-bearing, so they get named.

`CustomerRepository` and `ItemRepository` are the same one-proc-call shape as `AddAsync` above —
`sales.usp_Customer_GetById` and `sales.usp_Item_GetByIds`, both pure reads, so both open their own
connection through the factory rather than taking a transaction ([Phase 3b](03b-data-access.md)).

## 7. Wrapping the write in a transaction

This is your first Sales-order **write**, so it's where the transaction pattern you learned in
[Phase 3b](03b-data-access.md) earns its keep. Recall the one rule from there: *reads* grab a connection
directly; a *write that must be all-or-nothing* opens a transaction. A create is the textbook case — an
order is a header **plus** its lines, several `INSERT`s that have to land together or not at all.

Look back at how the two paths split in `CreateSalesOrderHandler` above:

- **All the reads happen first, with no transaction open.** `customers.GetAsync`, `items.GetForOrderAsync`,
  and `NextOrderNumberAsync` each grab a connection, read, and release it. Every read-only check — blocked
  customer, category rules, `EnsureWithinCreditLimit` — runs here, *before* any transaction exists, so you
  never hold locks while deciding. (The query slices in [Phase 10](10-queries-search.md) are *pure* reads, so
  they never open a transaction at all.)
- **Only the write is wrapped.** `AddAsync` (header + every line) runs inside the transaction, on `tx`:

```csharp
using var conn = factory.Create();
await conn.OpenAsync(ct);
using var tx = conn.BeginTransaction();
try
{
    var newId = await orders.AddAsync(order, tx, ct);   // header + lines, atomically
    tx.Commit();
    return newId;
}
catch
{
    tx.Rollback();
    throw;
}
```

The shape is **commit on success, roll back on throw** ([Phase 3b §6](03b-data-access.md)): if any guard
throws before `tx.Commit()`, the `catch` rolls back the half-written order — and even if something slips past
the `catch`, the `using` disposes the uncommitted transaction and rolls it back anyway. No orphaned header
either way. We lean on this hard in [Phase 7](07-submit-reserve-concurrency.md), where a submit reserves
stock across several rows in one transaction.

## 8. The endpoint (a Carter module, in the feature folder)

`Modules/Sales/Features/CreateSalesOrder/CreateSalesOrderEndpoint.cs` — the feature's *second* file, a
Carter `ICarterModule` living right next to its handler:

```csharp
// src/Modules/Sales/Features/CreateSalesOrder/CreateSalesOrderEndpoint.cs
using Carter;
using Erp.Shared.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

public sealed class CreateSalesOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/sales-orders", async (CreateSalesOrderCommand cmd, ISender sender, CancellationToken ct) =>
        {
            var id = await sender.Send(cmd, ct);
            return Results.Created($"/sales-orders/{id}", new { id });
        })
        .RequireAuthorization(p => p.RequireRole("SalesRep", "SalesManager", "Admin"));  // logged in, sales role
}
```

Thin as ever: turn the request into a command, send it, return `201 Created`. All the smarts are behind
`sender.Send`. `app.MapCarter()` in `Program.cs` discovers this endpoint automatically — you never edit a
central route list.

> 🧭 **From WebForms:** this replaces a `[WebMethod]` your jQuery `$.ajax` used to POST to. Same idea —
> "expose this operation over HTTP" — but the endpoint is a two-line adapter and the real work lives in a
> testable handler, not in the page.

## 9. How a real request flows (with sample data)

> ℹ️ **Heads-up on validation & error formatting.** Two cross-cutting pieces the trace below mentions —
> the `ValidationBehavior` that auto-runs the validator, and the global handler that turns a thrown
> `DomainException` into a clean HTTP `422` — are built in [Phase 11](11-cross-cutting.md) (that's the
> cross-cutting phase; the pipeline was designed for them back in Phase 2). If you're building strictly in
> order, the **happy path fully works now** (a valid order returns `201`), and a *rule violation* still
> correctly **blocks the write** (the transaction never commits) — it just surfaces as a raw `500` until you
> wire Phase 11's behaviors, which then make it a tidy `422`. Nothing here is broken; the polish is one phase
> away.

A SalesRep logged in as `rep` sends:

```http
POST /sales-orders     X-Api-Key: <key>     Authorization: Bearer <token>
{ "customerId": 2, "lines": [ { "itemId": 1, "quantity": 100 } ] }
```

Customer 2 is *Beta Industries* — credit limit ₱5,000, already owes ₱4,500, so only **₱500** of credit left.
Item 1 is *Widget* at ₱25. 100 × ₱25 = **₱2,500**. Watch:

```
0. ApiKeyMiddleware matches X-Api-Key against a known client → allowed through (Phase 4c §11).
1. Auth middleware validates the token → caller is 'rep', role SalesRep.
2. Endpoint builds CreateSalesOrderCommand, calls sender.Send.
3. (Once wired in Phase 11) LoggingBehavior logs "Handling CreateSalesOrderCommand".
4. (Once wired in Phase 11) ValidationBehavior: customerId 2 > 0 ✓, 1 line ✓, qty 100 > 0 ✓ → passes.
5. Handler loads Customer 2 → exists.
6. customer.EnsureNotBlocked() → Beta is not blocked ✓.
7. Loads Item 1 (Widget, General category, no role restriction, cap 1000).
8. Category.EnsureRoleAllowed("SalesRep") → General has no RequiredRole ✓.
9. Running qty for General = 100; EnsureQuantityAllowed(100) → 100 <= 1000 ✓.
10. order.AddLine(...) → order Total becomes ₱2,500.
11. customer.EnsureWithinCreditLimit(₱2,500) → 4500 + 2500 = 7000 > 5000 limit → THROWS ❌
12. Handler exits via exception; the transaction was never opened (and had it been, the catch/using rolls it back).
13. The exception becomes an HTTP 422 with "would exceed the credit limit" once Phase 11 formats it.
```

Change the quantity to `1` (total ₱25): step 11 passes (4500 + 25 = 4525 ≤ 5000), the transaction
commits, and the endpoint returns `201 Created { id: ... }`. **Same code, different data, correct
business outcome** — because the rules live in the right places.

### Checkpoint — create a real order ✅

Log in as `rep` (Phase 4), then create a valid order. Every request now carries **both** headers — the API key
(which app) and the bearer token (which user), exactly as [Phase 4c §11](04c-jwt-refresh-apikey.md) set up:

```bash
BASE=http://localhost:5234       # your port
KEY=web-live-abc123              # the API key you configured
TOKEN=...                        # accessToken from POST /auth/login

curl -X POST "$BASE/sales-orders" \
     -H "X-Api-Key: $KEY" -H "Authorization: Bearer $TOKEN" \
     -H "Content-Type: application/json" \
     -d '{"customerId":1,"lines":[{"itemId":1,"quantity":2}]}'      # Acme, 2 Widgets → 201 Created { id }
```

> ⚠️ Forget the `X-Api-Key` and you get `401 {"error":"Missing or invalid API key."}` — which looks exactly
> like an expired token but isn't. When a request 401s from here on, check the key before you check the token.

Verify it landed: `SELECT OrderNumber, Status, TotalAmount FROM sales.SalesOrder;` shows one `Draft`
(Status 0) order totalling ₱50. Try the blocked customer (`"customerId":3`, Gamma) or an over-limit order
(Beta with quantity 100) and confirm **no** row is written — the rule blocked the transaction. If a valid
order returns `201` and a rule-breaking one writes nothing, **Phase 6 is done and verifiable** (the clean
`422` body for the rejection arrives with Phase 11).

## 10. Why each component exists

| Component | Why |
|---|---|
| `CreateSalesOrderCommand` | The inputs, as one immutable request. |
| `CreateSalesOrderValidator` | Input well-formedness, before any work. |
| `CreateSalesOrderHandler` | Orchestrates the use case; delegates every rule to the domain. |
| `ISalesOrderRepository` etc. | Ports the module's `Domain/` declares; the module's `Infrastructure/` supplies the SQL. |
| `IDbConnectionFactory` + `IDbTransaction` | Makes the transaction boundary explicit; commit on success, roll back on throw. |
| `ICurrentUser` | Supplies the caller (to stamp `CreatedBy` and check category roles). |
| Endpoint | Thin HTTP adapter; enforces coarse "must be a sales role" auth. |

## 11. Common mistakes & best practices

- ❌ **Business rules in the validator** ("blocked customer" check in FluentValidation). ✅ Validator =
  shape only; rules = domain.
- ❌ **A policy `if` in the handler** (`if (customer.IsBlocked)`). ✅ `customer.EnsureNotBlocked()`.
- ❌ **N+1 database calls** — loading each item inside the loop. ✅ Load all items in one
  `GetForOrderAsync(itemIds)` before the loop.
- ❌ **Checking the category cap per-line instead of per-order.** ✅ Accumulate quantity per category
  across all lines (a customer could split a restricted item across two lines to dodge a per-line cap).
- ❌ **Forgetting to pass the transaction to the repository**, so inserts run outside it and can't roll
  back together. ✅ Thread `tx` through.
- ❌ **A naive `MAX(Id)+1` order number** — two concurrent creates can produce the same number. ✅
  `NextOrderNumberAsync` calls `sales.usp_SalesOrder_NextNumber`, backed by a SQL `SEQUENCE`
  ([Phase 3 §3](03-database-schema.md)), which hands out unique values safely under concurrency.
  *(Note: we ask for the number **before** the credit check, so an order that gets rejected still "uses up"
  a sequence value — leaving small gaps like `SO-2026-000123` then `-000125`. That's expected and harmless:
  sequences guarantee **uniqueness**, not **gapless** numbering. If your business genuinely needs
  gapless numbers, that's a separate, harder problem — don't try to solve it with a sequence.)*
- ✅ **Best practice: fail fast, before opening the transaction.** Notice most checks run *before*
  `BeginTransaction()`. Don't hold a database transaction open while doing validation you could have done first.

---

## ✅ Check Yourself

1. Which checks are in the validator and which in the domain — and what's the dividing principle?
2. How does an exception mid-handler cause the database changes to roll back — what does the `catch` do,
   and what still happens if the `catch` is somehow bypassed?
3. Why accumulate category quantity across the whole order instead of per line?
4. Trace what happens (and why) when Beta Industries orders 100 Widgets.

---

← Previous: [Phase 5 — The SalesOrder domain model](05-domain-model.md) · [Tutorial overview](00-START-HERE.md) · Next: [Phase 7 — Submit + atomic inventory reservation](07-submit-reserve-concurrency.md) →
