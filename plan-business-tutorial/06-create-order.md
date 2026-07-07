# Phase 6 — Create Sales Order (End to End, With Real Checks)

> 📎 Slow version of "the anatomy of a command slice":
> [beginner Phase 4](../plan-beginner-friendly/05-phase-4-create-order.md). Here we build a *real* one
> with customer blocking, category role/quantity restrictions, and a credit-limit check.

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

We're combining three ideas the earlier plans taught, now with teeth:

- **Two kinds of validation** ([beginner Phase 4 §2](../plan-beginner-friendly/05-phase-4-create-order.md)):
  *input validation* ("is customerId present, are quantities positive?") in the validator; *business
  rules* ("is the customer blocked, over credit limit?") in the domain.
- **Handlers orchestrate, the domain decides.** The handler loads data and calls domain methods; it
  holds no policy `if`s.
- **Explicit transaction** so the boundary is visible while learning.

## 3. The command, validator, and DTO

The whole feature is **two files** in `Modules/Sales/Features/CreateSalesOrder/`:
`CreateSalesOrderHandler.cs` (command + result + validator + handler) and `CreateSalesOrderEndpoint.cs`
(request + response + Carter endpoint). Here's the top of `CreateSalesOrderHandler.cs`:

```csharp
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

The `SalesOrder` repository interface belongs to the **module** (it's specific to this aggregate), in
`Modules/Sales/Domain` (a "port"); the generic data plumbing it uses — `IDbConnectionFactory`,
`IUnitOfWork` — lives in `Erp.Shared.Infrastructure` because every module needs it:

```csharp
public interface ISalesOrderRepository
{
    Task<string> NextOrderNumberAsync(CancellationToken ct);
    Task<long> AddAsync(SalesOrder order, IDbTransaction tx, CancellationToken ct);   // returns the DB-assigned id
    Task<SalesOrder?> GetAsync(SalesOrderId id, CancellationToken ct);
    Task UpdateStatusAsync(SalesOrder order, IDbTransaction tx, CancellationToken ct);
}

public interface ICustomerRepository
{
    Task<Customer?> GetAsync(CustomerId id, CancellationToken ct);
    // delta is a SIGNED decimal, not Money: approval adds the total (+), cancelling an approved order
    // reverses it (−). Money forbids negatives, so a balance *delta* can't be a Money. (Phase 8)
    Task AdjustBalanceAsync(CustomerId id, decimal delta, IDbTransaction tx, CancellationToken ct);
}

public interface IItemRepository
{
    // Loads the requested items WITH their category rules, in one round-trip.
    Task<IReadOnlyList<ItemForOrder>> GetForOrderAsync(IReadOnlyList<long> itemIds, CancellationToken ct);
}

// A small read shape the handler needs: item + its price + its category rules.
public sealed record ItemForOrder(long ItemId, bool IsActive, Money UnitPrice, ItemCategory Category);
```

> ❓ **You might be wondering: "Why does the interface live in Application, but the implementation in
> Infrastructure?"** The dependency rule. The handler (Application) needs *a way to load a customer*, so
> Application *declares the interface*. The actual Dapper/SQL code (Infrastructure) *implements* it. The
> handler depends on the interface, never on SQL. Same pattern as everything else —
> [beginner Phase 4 §5](../plan-beginner-friendly/05-phase-4-create-order.md).

## 5. The handler — where the business rules come together

This is the meat. Read every line; the comments mark which layer owns each decision.

```csharp
public sealed class CreateSalesOrderHandler(
    ICustomerRepository customers,
    IItemRepository items,
    ISalesOrderRepository orders,
    IUnitOfWork uow,
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
        await using var tx = await uow.BeginAsync(ct);
        var newId = await orders.AddAsync(order, tx.Transaction, ct);   // AddAsync hands back the DB id
        await tx.CommitAsync(ct);

        return newId;
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

`Modules/Sales/Infrastructure/SalesOrderRepository.cs` (abbreviated to the create path):

```csharp
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
boundary: `order.CustomerId.Value`, `(int)order.Status`, `line.UnitPrice.Amount`.

## 7. The Unit of Work (the transaction boundary)

`IUnitOfWork` gives the handler a visible transaction. Interfaces in `Erp.Shared.Infrastructure`, impls too;
the handler and query handlers depend only on the interfaces. `IDbConnectionFactory` hands out connections —
query handlers ([Phase 10](10-queries-search.md)) call `Create()` directly for reads; the unit of work uses
it to start a transaction for writes:

```csharp
public interface IDbConnectionFactory
{
    IDbConnection Create();   // returns a SqlConnection (opened below before use)
}

public interface IUnitOfWork
{
    Task<IAppTransaction> BeginAsync(CancellationToken ct);
}
public interface IAppTransaction : IAsyncDisposable
{
    IDbTransaction Transaction { get; }
    Task CommitAsync(CancellationToken ct);
}
```

```csharp
// Infrastructure
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

> ❓ **You might be wondering: "How does a failure roll back? I don't see a `Rollback` call."** The
> `await using var tx = ...` line means "dispose `tx` when the handler exits." If we reach
> `tx.CommitAsync`, the changes are saved. But if a domain rule throws *before* the commit (say,
> `EnsureWithinCreditLimit` fails), the handler exits early via the exception, `DisposeAsync` runs, and
> because we never committed, disposing the transaction **rolls it back automatically**. So "throw =
> rollback" falls out naturally. We'll lean on this hard in [Phase 7](07-submit-reserve-concurrency.md).

## 8. The endpoint (a Carter module, in the feature folder)

`Modules/Sales/Features/CreateSalesOrder/CreateSalesOrderEndpoint.cs` — the feature's *second* file, a
Carter `ICarterModule` living right next to its handler:

```csharp
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

A SalesRep logged in as `rep` sends:

```http
POST /sales-orders     Authorization: Bearer <token>
{ "customerId": 2, "lines": [ { "itemId": 1, "quantity": 100 } ] }
```

Customer 2 is *Beta Industries* — credit limit 5000, already owes 4500, so only **500** of credit left.
Item 1 is *Widget* at $25. 100 × $25 = **$2,500**. Watch:

```
1. Auth middleware validates the token → caller is 'rep', role SalesRep.
2. Endpoint builds CreateSalesOrderCommand, calls sender.Send.
3. LoggingBehavior logs "Handling CreateSalesOrderCommand".
4. ValidationBehavior: customerId 2 > 0 ✓, 1 line ✓, qty 100 > 0 ✓ → passes.
5. Handler loads Customer 2 → exists.
6. customer.EnsureNotBlocked() → Beta is not blocked ✓.
7. Loads Item 1 (Widget, General category, no role restriction, cap 1000).
8. Category.EnsureRoleAllowed("SalesRep") → General has no RequiredRole ✓.
9. Running qty for General = 100; EnsureQuantityAllowed(100) → 100 <= 1000 ✓.
10. order.AddLine(...) → order Total becomes $2,500.
11. customer.EnsureWithinCreditLimit($2,500) → 4500 + 2500 = 7000 > 5000 limit → THROWS ❌
12. Handler exits via exception; the transaction was never opened (or if opened, disposes → rollback).
13. The exception becomes an HTTP 422/400 with "would exceed the credit limit" (Phase 11 formats it).
```

Change the quantity to `1` (total $25): step 11 passes (4500 + 25 = 4525 ≤ 5000), the transaction
commits, and the endpoint returns `201 Created { id: ... }`. **Same code, different data, correct
business outcome** — because the rules live in the right places.

## 10. Why each component exists

| Component | Why |
|---|---|
| `CreateSalesOrderCommand` | The inputs, as one immutable request. |
| `CreateSalesOrderValidator` | Input well-formedness, before any work. |
| `CreateSalesOrderHandler` | Orchestrates the use case; delegates every rule to the domain. |
| `ISalesOrderRepository` etc. | Application's declared needs; Infrastructure supplies the SQL. |
| `IUnitOfWork` / `IAppTransaction` | Makes the transaction boundary explicit and gives auto-rollback. |
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
  back together. ✅ Thread `tx.Transaction` through.
- ❌ **A naive `MAX(Id)+1` order number** — two concurrent creates can produce the same number. ✅
  `NextOrderNumberAsync` calls `sales.usp_SalesOrder_NextNumber`, backed by a SQL `SEQUENCE`
  ([Phase 3 §3](03-database-schema.md)), which hands out unique values safely under concurrency.
  *(Note: we ask for the number **before** the credit check, so an order that gets rejected still "uses up"
  a sequence value — leaving small gaps like `SO-2026-000123` then `-000125`. That's expected and harmless:
  sequences guarantee **uniqueness**, not **gapless** numbering. If your business genuinely needs
  gapless numbers, that's a separate, harder problem — don't try to solve it with a sequence.)*
- ✅ **Best practice: fail fast, before opening the transaction.** Notice most checks run *before*
  `BeginAsync`. Don't hold a database transaction open while doing validation you could have done first.

---

## ✅ Check Yourself

1. Which checks are in the validator and which in the domain — and what's the dividing principle?
2. How does an exception mid-handler cause the database changes to roll back, with no explicit
   `Rollback` call?
3. Why accumulate category quantity across the whole order instead of per line?
4. Trace what happens (and why) when Beta Industries orders 100 Widgets.

> 👉 Next: [Phase 7 — Submit for approval + atomic inventory reservation + rollback](07-submit-reserve-concurrency.md).
