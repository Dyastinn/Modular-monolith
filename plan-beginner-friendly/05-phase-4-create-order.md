# Phase 4 — Your First Real Feature: Create a Sales Order (end to end)

> 📄 Expert version: [`../plan-Opus/05-phase-4-create-order.md`](../plan-Opus/05-phase-4-create-order.md)

**In one sentence:** We wire one feature all the way through — web → validation → handler → domain →
database → response — as **two small files**, so the *shape of a feature* becomes concrete. Every later
feature copies this shape.

**What you'll build:** The `CreateSalesOrder` feature (two files), a `ValidationBehavior`, the database
connection plumbing (in `Shared.Infrastructure`), a `SalesOrderRepository` (Dapper + stored procedures, in
the module's `Infrastructure/`), the ID type-handlers, the `usp_SalesOrder_Insert` stored procedure, and
explicit transaction handling.

**Before you start:** Phases 1–3 done, and a dev SQL Server database with the schema (see Step 6).

---

## Step 1 — The anatomy of a feature: two files

A command is a request that *changes* something. Here's the **entire** folder for `CreateSalesOrder` — two
files in the module, plus the shared plumbing and SQL they lean on:

```
Modules/Sales/Features/CreateSalesOrder/
    CreateSalesOrderHandler.cs    <- Command + Result + Validator + Handler   (the behavior)
    CreateSalesOrderEndpoint.cs   <- Request + Response + Carter endpoint       (the web edge)
Modules/Sales/Infrastructure/
    SalesOrderRepository.cs       <- saves/loads SalesOrders via Dapper + stored procs
    SalesOrderIdTypeHandler.cs    <- teaches Dapper how to read/write the strongly-typed IDs
    Sql/usp_SalesOrder_Insert.sql
Shared/Erp.Shared.Infrastructure/Persistence/
    IDbConnectionFactory.cs, IUnitOfWork.cs (+ SQL Server impls)  <- shared db plumbing
```

> 🔑 **Two files, not four.** We deliberately keep a command's *behavior* (Command, Result, Validator,
> Handler) in **one** file and its *web edge* (Request, Response, endpoint) in **one** file. Open the
> folder, see the whole feature. We don't split Command/Validator/Handler into three files (that scatters
> what you read together), and we don't merge the endpoint in (the web edge has different dependencies and
> is tested differently).

We add a few tiny "marker" interfaces in `Erp.Shared.Messaging` just to make intent obvious (they add no
behavior — they only narrow `IRequest<T>` / `IRequestHandler<,>`):

```csharp
// Request markers — is this request a change or a read?
public interface ICommand<TResponse> : IRequest<TResponse> { }  // "I change something"
public interface IQuery<TResponse>   : IRequest<TResponse> { }  // "I only read"

// Matching handler markers — read better than IRequestHandler and let us target behaviors
// at just commands (e.g. "wrap every ICommandHandler in a transaction") later if we want.
public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse> { }
public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse> { }
```

That's the whole definition — `ICommandHandler<,>` / `IQueryHandler<,>` are literally `IRequestHandler<,>`
with a clearer name and a constraint, nothing more. The mediator still resolves and runs them as
`IRequestHandler<,>`, so no other code changes. `CreateSalesOrder` is a **command** that returns the new
order's id.

### File 1 — `CreateSalesOrderHandler.cs` (the whole behavior)

```csharp
namespace Erp.Modules.Sales.Features.CreateSalesOrder;

// ── Command: the inputs ────────────────────────────────────────────────
public sealed record CreateSalesOrderCommand(long CustomerId, IReadOnlyList<OrderLineInput> Lines)
    : ICommand<CreateSalesOrderResult>;

public sealed record OrderLineInput(long ProductId, int Quantity);

// ── Result: what the handler hands back ────────────────────────────────
public sealed record CreateSalesOrderResult(long SalesOrderId);

// ── Validator: is the request well-formed? (NOT business rules) ─────────
public sealed class CreateSalesOrderValidator : AbstractValidator<CreateSalesOrderCommand>
{
    public CreateSalesOrderValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.Quantity).GreaterThan(0);
            l.RuleFor(x => x.ProductId).GreaterThan(0);
        });
    }
}

// ── Handler: coordinate the steps, delegate rules to the domain ────────
public sealed class CreateSalesOrderHandler(
    ICustomerRepository customers,
    ISalesOrderRepository orders,
    IPricingService pricing,          // ← introduced properly in Phase 5; for now: "give me an item's price"
    IUnitOfWork uow)
    : ICommandHandler<CreateSalesOrderCommand, CreateSalesOrderResult>
{
    public async Task<CreateSalesOrderResult> Handle(CreateSalesOrderCommand cmd, CancellationToken ct)
    {
        var customerId = new CustomerId(cmd.CustomerId);

        var customer = await customers.GetAsync(customerId, ct)
            ?? throw new NotFoundException($"Customer {cmd.CustomerId} not found.");
        customer.EnsureCanOrder();          // domain method: throws if on credit hold / inactive

        var order = SalesOrder.Create(customerId);
        foreach (var line in cmd.Lines)
        {
            var unitPrice = await pricing.GetUnitPrice(new ProductId(line.ProductId), ct);
            order.AddLine(new ProductId(line.ProductId), new Quantity(line.Quantity), unitPrice);
        }

        await using var tx = await uow.BeginAsync(ct);   // transaction boundary — kept visible (Step 7)
        await orders.AddAsync(order, tx.Inner, ct);        // save ON the handler's transaction (Step 7)
        await tx.CommitAsync(ct);

        return new CreateSalesOrderResult(order.Id.Value);
    }
}
```

> ℹ️ **About `IPricingService`:** it's a small interface — "given a `ProductId`, give me its `Money` unit
> price." Be precise about what it *is*: because it fetches data from a price list, it's really a **read port
> (data access) that the domain declares and Infrastructure implements** — the same shape as a repository, not
> a "domain service." (A *true* domain service holds stateless domain *logic* that spans several aggregates
> and does **no** I/O — see the corrected note in [Phase 5 §2](06-phase-5-inventory-concurrency.md).) For
> Phase 4, read it as a simple price lookup; a stub that returns a fixed price is fine to get this running.

### File 2 — `CreateSalesOrderEndpoint.cs` (the web edge, via Carter)

```csharp
namespace Erp.Modules.Sales.Features.CreateSalesOrder;

// ── Request: the HTTP body shape (here it matches the command, so it's thin) ──
public sealed record CreateSalesOrderRequest(long CustomerId, List<OrderLineInput> Lines);

// ── Response: the HTTP response shape ─────────────────────────────────
public sealed record CreateSalesOrderResponse(long SalesOrderId);

// ── Endpoint: a Carter module living right next to its handler ─────────
public sealed class CreateSalesOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/sales-orders", async (
            CreateSalesOrderRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateSalesOrderCommand(request.CustomerId, request.Lines), ct);
            return Results.Created($"/sales-orders/{result.SalesOrderId}",
                new CreateSalesOrderResponse(result.SalesOrderId));
        });
}
```

> ❓ **"Why have both a `Request` and a `Command` when they look the same?"** Right now they *do* look the
> same, and that feels redundant. Keep them separate anyway, because they answer to different masters: the
> `Request` is the shape of the *HTTP contract* (what the outside world sends), and the `Command` is the
> shape of the *internal operation*. They drift apart the moment the web needs a field the command doesn't
> (or vice versa) — e.g. the endpoint later reads the caller's user id from the JWT, not the body. When the
> two are genuinely identical and you want less ceremony, it's fine to bind the command directly; we keep
> them split here so you can *see* the seam.

## Step 2 — Two kinds of "validation" (a core lesson — read carefully)

"Validation" gets used for two *completely different* things, and they live in different places. Confusing
them is one of the most common mistakes.

| Kind | The question it answers | Where it lives | Example |
|---|---|---|---|
| **Input validation** | "Is this request even well-formed?" | The **Validator** (in the handler file, runs before the handler) | `CustomerId > 0`; at least one line; quantities present |
| **Business rule** | "Is this operation *allowed* given reality?" | The **Domain** (on `SalesOrder`/`Customer`) | "only draft orders can be submitted"; "customer must be active" |

> 🔑 **The distinction:** the validator asks *"should this request even reach the handler?"* The domain asks
> *"is this operation legal given the actual state of the world?"* Mixing them scatters your rules and makes
> the domain no longer the single source of truth. **Never put business rules in the validator.**

> ℹ️ We use **FluentValidation** because it's the idiomatic choice and reads clearly. Unlike the mediator
> (which we hand-roll to learn the pattern), input validation is well-solved plumbing — using the library
> is the production-quality call, not a shortcut.

## Step 3 — The ValidationBehavior (the mediator pipeline paying off)

Remember pipeline behaviors from Phase 2? Here's the first real payoff. Instead of every handler calling its
own validator, **one behavior validates every command** automatically, before the handler runs. It lives in
`Shared.Messaging` (every module wants it):

```csharp
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var failures = validators
            .Select(v => v.Validate(request))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();
        if (failures.Count != 0) throw new ValidationException(failures);  // stop before the handler
        return await next();                                                // all good → continue
    }
}
```

**Order matters** (recall Phase 2): **Logging outermost** (so you log even requests that fail validation),
then **Validation**, then the handler. Register them outer-to-inner in that order.

## Step 4 — The handler (coordination only, no business rules)

Look back at the handler in File 1. Its job is to *coordinate the steps*. It holds **zero business rules** —
those live in the domain. Line by line:

```csharp
var customerId = new CustomerId(cmd.CustomerId);
```
`cmd.CustomerId` is a plain `long` (say `42`) from the web. We immediately wrap it in `CustomerId` so the
compiler stops us mixing it with a `SalesOrderId`. (Strongly-typed IDs from Phase 3, cashing in.)

```csharp
var customer = await customers.GetAsync(customerId, ct)
    ?? throw new NotFoundException($"Customer {cmd.CustomerId} not found.");
```
Load the customer. The `?? throw ...` means "if there's no such customer, stop now with a not-found error."

```csharp
customer.EnsureCanOrder();
```
Ask the **domain** the business question: "is this customer allowed to order?" The rule lives on `Customer`;
the handler just *asks*. If the answer is no, `EnsureCanOrder` throws and we stop.

```csharp
var order = SalesOrder.Create(customerId);
foreach (var line in cmd.Lines)
{
    var unitPrice = await pricing.GetUnitPrice(new ProductId(line.ProductId), ct);
    order.AddLine(new ProductId(line.ProductId), new Quantity(line.Quantity), unitPrice);
}
```
Build the order *through the domain*. `AddLine` itself enforces "only drafts can have lines" and (via
`Quantity`) "quantity must be positive." The handler checks none of that; the domain does.

```csharp
await using var tx = await uow.BeginAsync(ct);
await orders.AddAsync(order, ct);
await tx.CommitAsync(ct);
```
Open a transaction (Step 7), save the whole order, commit. If the header saved but a line failed, the commit
never happens and the database undoes everything — you never get half an order.

Notice what the handler *does* (orchestrate: load, build, save, commit) and what it does **not** do (decide
rules). **If you ever catch a business `if` sneaking into a handler, stop: it belongs in the domain.**

> ❓ **"The handler has a `foreach` and a null check — aren't those `if`s?"** Handlers *do* have
> *orchestration* logic (loops, null checks, "load then save"). What they must not have is *business policy*
> — decisions about what the business allows ("reject orders over $50k"). "Loop over the lines" is
> coordination; "reject orders over $50k" is policy. First is fine in a handler; second belongs in the
> domain.

## Step 5 — Saving with Dapper + a stored procedure (in the module's Infrastructure)

**No generic repository.** `ISalesOrderRepository` is specific to the `SalesOrder` aggregate, and lives in
`Sales/Infrastructure/`:

```csharp
public interface ISalesOrderRepository
{
    Task AddAsync(SalesOrder order, IDbTransaction tx, CancellationToken ct);      // writes run on the handler's tx
    Task<SalesOrder?> GetAsync(SalesOrderId id, CancellationToken ct);             // a read — opens its own connection
    Task UpdateAsync(SalesOrder order, IDbTransaction tx, CancellationToken ct);   // used in Phase 5/7
}
```

The Dapper implementation calls stored procedures and maps rows into the aggregate. With no ORM change
tracker, saving is **explicit** — you hand over the whole aggregate and the repository writes the header plus
all the lines:

```csharp
public sealed class SalesOrderRepository(IDbConnectionFactory factory) : ISalesOrderRepository
{
    public async Task AddAsync(SalesOrder order, IDbTransaction tx, CancellationToken ct)
    {
        var conn = tx.Connection!;   // reuse the handler's transaction's connection — do NOT open a new one,
                                     // or the inserts wouldn't be part of the transaction the handler committed.
        var orderId = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "sales.usp_SalesOrder_Insert",   // always schema-qualify: the proc lives in the sales schema
            new { order.CustomerId /* mapped via a TypeHandler */, Status = (int)order.Status },
            transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));

        foreach (var line in order.Lines)
            await conn.ExecuteAsync(new CommandDefinition(
                "sales.usp_SalesOrderLine_Insert",
                new { SalesOrderId = orderId, line.ProductId, Quantity = line.Quantity.Value,
                      UnitPrice = line.UnitPrice.Amount },
                transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }
    // GetAsync: call sales.usp_SalesOrder_Get + sales.usp_SalesOrderLine_GetByOrder, rebuild via the private constructor.
}
```

**The strongly-typed ID mapping** — the ~10-line cost from Phase 3. Dapper needs a "type handler" per ID so
`SalesOrderId` maps to/from the database column:

```csharp
public sealed class SalesOrderIdTypeHandler : SqlMapper.TypeHandler<SalesOrderId>
{
    public override SalesOrderId Parse(object value) => new((long)value);
    public override void SetValue(IDbDataParameter p, SalesOrderId value) => p.Value = value.Value;
}
// Registered once at startup: SqlMapper.AddTypeHandler(new SalesOrderIdTypeHandler());
```

> 📦 **Where does this get registered?** In the module's `DependencyInjection.cs` (`AddSalesModule`): it
> registers `ISalesOrderRepository → SalesOrderRepository`, the customer/pricing repos, and the Dapper type
> handlers. The module wires up *its own* Infrastructure — the Api host never sees these types.

## Step 6 — What goes in the stored procedure (and what doesn't)

`usp_SalesOrder_Insert` does **saving and data-integrity**, *not* business policy:

```sql
CREATE PROCEDURE sales.usp_SalesOrder_Insert   -- created IN the sales schema; call it as "sales.usp_SalesOrder_Insert"
    @CustomerId BIGINT,
    @Status     INT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO sales.SalesOrder (CustomerId, Status, CreatedUtc)
    VALUES (@CustomerId, @Status, SYSUTCDATETIME());
    SELECT CAST(SCOPE_IDENTITY() AS BIGINT);   -- returns the new SalesOrderId
END
```

Notice the `sales.` schema prefix — each module owns its own **database schema** (`sales`, later
`inventory`), the database-level echo of the module walls. The schema enforces integrity the app shouldn't
have to trust: `CustomerId` is a foreign key, `Status` is constrained, quantities are `> 0` via a CHECK.
**Integrity lives in SQL; policy ("only draft can be submitted") lives in the domain.** No submit/approval
logic here — that would create the "split-brain" problem where the same rule exists in two places and
drifts.

## Step 7 — Transactions: why we keep them visible

We deliberately keep the transaction boundary **right there in the handler**
(`uow.BeginAsync`/`CommitAsync`) rather than hiding it in an automatic behavior:

- **Automatic (behavior-based) transactions** wrap every command for you. Convenient — but they *hide where
  the boundary is*, wrap queries you didn't mean to wrap, and complicate domain-event dispatch ordering.
- **Explicit transactions** cost a couple of lines per command, but the boundary is *obvious* — exactly what
  you want while learning where it belongs. For `CreateSalesOrder`, the boundary spans "insert the header +
  all the lines."

Here are the two small interfaces the handler leans on. They live in `Shared.Infrastructure` (with their SQL
Server implementations); the handler depends only on the *interface*:

```csharp
public interface IUnitOfWork
{
    Task<IAppTransaction> BeginAsync(CancellationToken ct);   // opens a connection + starts a transaction
}

public interface IAppTransaction : IAsyncDisposable
{
    IDbTransaction Inner { get; }               // the raw transaction to hand to repositories (and, in Phase 5, the stock contract)
    Task CommitAsync(CancellationToken ct);     // make the changes permanent
    // If you dispose WITHOUT calling CommitAsync (e.g. a rule threw), disposing rolls the transaction back —
    // so "throw = rollback" falls out for free. That's why the handler uses `await using var tx = …`.
}
```

> ℹ️ **Naming heads-up:** the [business tutorial](../plan-business-tutorial/06-create-order.md) calls this
> same property `Transaction` instead of `Inner`. They're the identical concept — the raw `IDbTransaction` you
> pass to a repository or the stock contract. Pick one name and use it everywhere in *your* build; we use
> `Inner` throughout this course.

> Later, once this is second nature, promoting transactions to a behavior is a reasonable refactor — but do
> it *after* you've felt where the boundary goes.

## Step 8 — Watch one real request flow through the whole system

A user submits this over the web:

```http
POST /sales-orders
{ "customerId": 42, "lines": [ { "productId": 7, "quantity": 3 } ] }
```

The journey, step by step:

1. **Carter endpoint** (`CreateSalesOrderEndpoint`) receives the JSON as a `CreateSalesOrderRequest`, builds
   a `CreateSalesOrderCommand`, calls `sender.Send(command)`.
2. **Mediator** looks up the handler for `CreateSalesOrderCommand` → `CreateSalesOrderHandler`, wraps it in
   behaviors.
3. **LoggingBehavior** logs `"Handling CreateSalesOrderCommand"` and starts a timer.
4. **ValidationBehavior** runs `CreateSalesOrderValidator`: `42 > 0`? ✓ ≥ 1 line? ✓ `quantity 3 > 0`? ✓
   `productId 7 > 0`? ✓ → calls `next()`, the handler runs. *(If customerId had been `0`, it throws here and
   the handler never runs.)*
5. **Handler** wraps `42` into `CustomerId(42)`, loads Customer 42 → active, so `EnsureCanOrder()` doesn't
   throw.
6. **Handler** calls `SalesOrder.Create(...)` → a new `Draft` order with a `SalesOrderCreated` event
   recorded.
7. **Handler** looks up Product 7's price (`$19.99`), then `order.AddLine(...)`. `AddLine` checks "am I a
   draft?" ✓. `Total` is now `$59.97` (computed, not stored).
8. **Handler** opens a transaction, calls `orders.AddAsync(order)`.
9. **Repository** calls `usp_SalesOrder_Insert` → new id `1001`; then loops the lines into
   `usp_SalesOrderLine_Insert`.
10. **Handler** commits → both rows permanent together.
11. **LoggingBehavior** logs `"Handled CreateSalesOrderCommand in 8ms"`.
12. **Endpoint** returns `201 Created` with `{ "salesOrderId": 1001 }`.

If you can picture that flow — the numbers moving through each piece, each doing its one job — you
understand this phase. Everything else is a variation on this trip.

> ❓ **"Where did `Status = 0` come from? I wrote `Draft`."** `SalesOrderStatus` is an enum stored as a
> number: `Draft = 0, Submitted = 1, Cancelled = 2, Completed = 3`. The repository converts the enum to its
> number when saving and back when loading. The domain always deals in the readable `Draft`; only the
> database sees the `0`.

## What to produce in this phase

- `ICommand<T>`/`IQuery<T>`/`ICommandHandler<,>` markers in `Shared.Messaging`.
- `CreateSalesOrderHandler.cs` (Command + Result + Validator + Handler) and
  `CreateSalesOrderEndpoint.cs` (Request + Response + Carter endpoint).
- `ValidationBehavior<,>`, registered in deliberate order relative to `LoggingBehavior`.
- `IDbConnectionFactory`, `IUnitOfWork` (interfaces) + SQL Server impls in `Shared.Infrastructure`.
- `ISalesOrderRepository` + `SalesOrderRepository` (Dapper, stored procs) + ID type-handlers, registered in
  `AddSalesModule`.
- `ICustomerRepository` + impl, with `Customer.EnsureCanOrder()` on the domain side.
- SQL: minimal `sales` schema for `SalesOrder`, `SalesOrderLine`, `Customer`, `Product`, plus the insert/get
  procs.
- `POST /sales-orders` works end to end; the dev connection string in `appsettings.Development.json`.

**NOT now:** inventory reservation, stock deduction, the second module, submit/approval, concurrency,
pricing rules beyond a simple lookup, or queries beyond `GetAsync`.

## Common mistakes to avoid

- **Splitting the feature into four files.** Command, Result, Validator, Handler stay in *one* file; the
  endpoint in *one* file. Don't scatter them.
- **Business `if`s creeping into the handler.** `if (status != Draft)` in a handler → stop; that belongs on
  `SalesOrder`.
- **A generic `Repository<T>`.** Forbidden — it forces a lowest-common-denominator API and pushes you back
  toward dumb-data-bag domains.
- **Adding public setters to please Dapper.** Rebuild via the private constructor.
- **Submit/approval logic sneaking into `usp_SalesOrder_Insert`.** That proc does persistence only.

---

## ✅ Check Yourself

1. **What two files make up a feature, and what's in each — and why that split rather than four files?**
2. **What's the difference between input validation and a business rule, and where does each live?**
3. **Trace a `POST /sales-orders` request through every step, naming what each does.**
4. **Why does the handler contain no business `if`s, and where did they go?**
5. **What belongs in `usp_SalesOrder_Insert`, and what deliberately does not?**

---

## 👨‍🏫 Answer Key

**1. The two files and the split.**
`CreateSalesOrderHandler.cs` holds the **behavior**: Command, Result, Validator, Handler — readable and
unit-testable with no web server. `CreateSalesOrderEndpoint.cs` holds the **web edge**: Request, Response,
and the Carter endpoint. Two files means you grasp the whole feature by opening one folder. Four separate
files would scatter what you read together; one giant file would bury the behavior/web-edge seam.

**2. Input validation vs. business rule.**
- **Input validation** — "is this request *well-formed*?" (CustomerId > 0, ≥ 1 line, quantities present).
  Lives in the **Validator**, runs *before* the handler.
- **Business rule** — "is this operation *allowed* given reality?" ("customer must be active," "only draft
  orders can be submitted"). Lives in the **Domain**.
Never put business rules in the validator, or the domain stops being the source of truth.

**3. Trace `POST /sales-orders`.**
Carter endpoint → builds command → `sender.Send` → Logging → Validation (throws if malformed) → Handler
(load Customer, `EnsureCanOrder`, `SalesOrder.Create` + `AddLine`, open transaction, `AddAsync`, commit) →
Repository maps to stored-proc params and calls `usp_SalesOrder_Insert`/`usp_SalesOrderLine_Insert` →
database inserts with FK/CHECK backstops → new id flows back as `201 Created`.

**4. Why no business `if`s in the handler, and where did they go?**
Business rules belong in the **Domain**, where they have one guardian. The handler only orchestrates (load,
build, save, commit). The rules moved onto the domain objects: `customer.EnsureCanOrder()`, the draft-only
check inside `SalesOrder.AddLine`, the positivity check inside `Quantity`. A policy `if` in a handler is a
smell — move it onto the entity.

**5. What belongs in `usp_SalesOrder_Insert`, and what doesn't?**
**Belongs:** persistence and data-integrity — the `INSERT`, returning the new id, letting the schema enforce
FKs/CHECKs. **Does not belong:** business policy like submit/approval logic. Policy in the proc creates
"split-brain" — the same rule in two places that drift. Integrity in SQL; policy in the domain.

> ✅ **You're ready for Phase 5 if:** you can trace a request through the steps *out loud*, name what's in
> each of the two files, and firmly say which "validation" goes where. This slice is the template every later
> feature copies.
