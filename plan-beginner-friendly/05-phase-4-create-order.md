# Phase 4 — Your First Real Feature: Create an Order (end to end)

> 📄 Expert version: [`../plan-Opus/05-phase-4-create-order.md`](../plan-Opus/05-phase-4-create-order.md)

**In one sentence:** We wire one feature all the way through — web → validation → handler → domain →
database → response — so the *shape of a feature* becomes concrete. Every later feature copies this
shape.

**What you'll build:** The `CreateOrder` feature, a `ValidationBehavior`, the database connection
plumbing, an `OrderRepository` (Dapper + stored procedures), the ID type-handlers, the
`usp_Order_Insert` stored procedure, and explicit transaction handling.

**Before you start:** Phases 1–3 done, and a dev SQL Server database with the schema (see Step 6).

---

## Step 1 — The anatomy of a "command" feature

A command is a request that *changes* something. Here's the full folder layout for `CreateOrder`:

```
Application/Orders/CreateOrder/
    CreateOrderCommand.cs     <- the inputs (a record). It's an ICommand<OrderId>.
    CreateOrderValidator.cs   <- checks the input is well-formed (NOT business rules)
    CreateOrderHandler.cs     <- the steps: load helpers, run the domain, save, return
Api/Endpoints/Orders/
    CreateOrderEndpoint.cs    <- POST /orders → mediator
Infrastructure/Persistence/Orders/
    OrderRepository.cs        <- saves/loads Orders using Dapper + stored procedures
db/
    usp_Order_Insert.sql
```

We add two tiny "marker" interfaces just to make intent obvious in the folder tree (they add no
behavior — they just extend `IRequest<T>`):

```csharp
public interface ICommand<TResponse> : IRequest<TResponse> { }  // "I change something"
public interface IQuery<TResponse>   : IRequest<TResponse> { }  // "I only read"
```

`CreateOrder` is a **command** that returns the new `OrderId`.

## Step 2 — Two kinds of "validation" (a core lesson — read carefully)

The word "validation" gets used for two *completely different* things, and they live in different
places. Confusing them is one of the most common mistakes.

| Kind | The question it answers | Where it lives | Example |
|---|---|---|---|
| **Input validation** | "Is this request even well-formed?" | The **Validator** (runs before the handler) | `CustomerId > 0`; at least one line; quantities present |
| **Business rule** | "Is this operation *allowed* given reality?" | The **Domain** (on the `Order`/`Customer`) | "only draft orders can be booked"; "customer must be active" |

> 🔑 **The distinction:** the validator asks *"should this request even reach the handler?"* The domain
> asks *"is this operation legal given the actual state of the world?"* Mixing them scatters your rules
> and makes the domain no longer the single source of truth. **Never put business rules in the
> validator.**

```csharp
public sealed class CreateOrderValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.Quantity).GreaterThan(0);
            l.RuleFor(x => x.MaterialId).GreaterThan(0);
        });
    }
}
```

> ℹ️ We use **FluentValidation** (a small, free library) because it's the idiomatic choice. If you'd
> rather not add a dependency, a validator is just "a method that returns a list of errors" — you can
> hand-roll it. The behavior below doesn't care which you pick.

## Step 3 — The ValidationBehavior (the mediator pipeline paying off)

Remember pipeline behaviors from Phase 2? Here's the first real payoff. Instead of every handler calling
its own validator, **one behavior validates every command** automatically, before the handler runs:

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

**Order matters** (recall Phase 2). The sensible order here is: **Logging outermost** (so you log even
requests that fail validation), then **Validation**, then the handler. Register them outer-to-inner in
that order.

## Step 4 — The handler (coordination only, no business rules)

The handler's job is to *coordinate the steps*. It holds **zero business rules** — those live in the
domain. Watch:

```csharp
public sealed class CreateOrderHandler(
    ICustomerRepository customers,
    IOrderRepository orders,
    IPricingService pricing,          // proper intro in Phase 5; here: just look up a unit price
    IUnitOfWork uow)
    : IRequestHandler<CreateOrderCommand, OrderId>
{
    public async Task<OrderId> Handle(CreateOrderCommand cmd, CancellationToken ct)
    {
        var customerId = new CustomerId(cmd.CustomerId);

        // Load the customer, then ASK THE DOMAIN whether they can order.
        var customer = await customers.GetAsync(customerId, ct)
            ?? throw new NotFoundException($"Customer {cmd.CustomerId} not found.");
        customer.EnsureCanOrder();          // domain method: throws if on credit hold / inactive

        // Build the order through the domain (which enforces its own rules).
        var order = Order.Create(customerId);
        foreach (var line in cmd.Lines)
        {
            var unitPrice = await pricing.GetUnitPrice(new MaterialId(line.MaterialId), ct);
            order.AddLine(new MaterialId(line.MaterialId), new Quantity(line.Quantity), unitPrice);
        }

        // The transaction boundary — kept VISIBLE on purpose (see Step 7).
        await using var tx = await uow.BeginAsync(ct);
        await orders.AddAsync(order, ct);
        await tx.CommitAsync(ct);

        return order.Id;   // (domain events get dispatched after commit — full story in Phase 7)
    }
}
```

Notice what the handler does (*orchestrate*: load customer, build order, save, commit) and what it
does **not** do (*decide rules*). Every rule check — `EnsureCanOrder`, the draft-only check inside
`AddLine`, the positive-quantity check inside `Quantity` — happens in the domain. **If you ever catch a
business `if` sneaking into a handler, stop: it belongs in the domain.**

## Step 5 — Saving with Dapper + a stored procedure

**No generic repository.** `IOrderRepository` is specific to the `Order` aggregate:

```csharp
public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken ct);
    Task<Order?> GetAsync(OrderId id, CancellationToken ct);   // rebuilds the whole aggregate
    Task UpdateAsync(Order order, CancellationToken ct);       // used in Phase 5/7
}
```

The Dapper implementation calls stored procedures and maps rows into the aggregate. Because we have no
"change tracker" (that's an ORM feature we skipped), saving is **explicit** — you hand over the whole
aggregate and the repository writes the header plus all the lines:

```csharp
public sealed class OrderRepository(IDbConnectionFactory factory) : IOrderRepository
{
    public async Task AddAsync(Order order, CancellationToken ct)
    {
        using var conn = factory.Create();
        // The repository maps the order to proc parameters. It contains NO business rules.
        var orderId = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "usp_Order_Insert",
            new { order.CustomerId /* mapped via a TypeHandler */, Status = (int)order.Status },
            commandType: CommandType.StoredProcedure, cancellationToken: ct));

        foreach (var line in order.Lines)
            await conn.ExecuteAsync(new CommandDefinition(
                "usp_OrderLine_Insert",
                new { OrderId = orderId, line.MaterialId, Quantity = line.Quantity.Value,
                      UnitPrice = line.UnitPrice.Amount },
                commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }
    // GetAsync: call usp_Order_Get + usp_OrderLine_GetByOrder, rebuild via the private constructor.
}
```

**The strongly-typed ID mapping** — remember the ~10-line cost mentioned in Phase 3? Here it is. Dapper
needs a "type handler" per ID so `OrderId` maps to/from the database column:

```csharp
public sealed class OrderIdTypeHandler : SqlMapper.TypeHandler<OrderId>
{
    public override OrderId Parse(object value) => new((long)value);
    public override void SetValue(IDbDataParameter p, OrderId value) => p.Value = value.Value;
}
// Registered once at startup: SqlMapper.AddTypeHandler(new OrderIdTypeHandler());
```

## Step 6 — What goes in the stored procedure (and what doesn't)

`usp_Order_Insert` does **saving and data-integrity**, *not* business policy:

```sql
CREATE PROCEDURE usp_Order_Insert
    @CustomerId BIGINT,
    @Status     INT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.[Order] (CustomerId, Status, CreatedUtc)
    VALUES (@CustomerId, @Status, SYSUTCDATETIME());
    SELECT CAST(SCOPE_IDENTITY() AS BIGINT);   -- returns the new OrderId
END
```

The **database schema** enforces integrity that the app shouldn't have to trust: `CustomerId` is a
foreign key, `Status` is constrained (a CHECK or lookup table), quantities are `> 0` via a CHECK as a
backstop. **Integrity lives in SQL; policy ("only draft can be booked") lives in the domain.** No
booking or approval logic here — putting it here would create the "split-brain" problem where the same
rule exists in two places and drifts.

## Step 7 — Transactions: why we keep them visible

We deliberately keep the transaction boundary **right there in the handler**
(`uow.BeginAsync` / `CommitAsync`) rather than hiding it inside an automatic behavior. Here's the
honest trade-off:

- **Automatic (behavior-based) transactions** wrap every command in a transaction for you. Convenient
  and DRY — but they *hide where the boundary is*, wrap queries you didn't mean to wrap, and complicate
  the ordering of domain-event dispatch.
- **Explicit transactions** cost a couple of lines per command, but the boundary is *obvious* — which
  is exactly what you want while you're learning where it belongs. For `CreateOrder`, the boundary
  spans "insert the header + all the lines."

The `IUnitOfWork`/`IDbConnectionFactory` implementations live in Infrastructure; the handler only
depends on the *interface*, so the dependency rule stays intact.

> Later, once this is second nature, promoting transactions to a behavior is a reasonable refactor —
> but do it *after* you've felt where the boundary goes, not before.

## What to produce in this phase

- `ICommand<T>`/`IQuery<T>` markers; `CreateOrderCommand`, `CreateOrderValidator`, `CreateOrderHandler`.
- `ValidationBehavior<,>`, registered in deliberate order relative to `LoggingBehavior`.
- `IDbConnectionFactory`, `IUnitOfWork` (interfaces) + SQL Server implementations.
- `IOrderRepository` + `OrderRepository` (Dapper, stored procs), ID type-handlers registered.
- `ICustomerRepository` + impl, with `Customer.EnsureCanOrder()` on the domain side (a minimal
  `Customer` is fine here; expand in Phase 5).
- SQL: minimal schema for `Order`, `OrderLine`, `Customer`, `Material`, plus `usp_Order_Insert`,
  `usp_OrderLine_Insert`, `usp_Order_Get`, `usp_OrderLine_GetByOrder`.
- `POST /orders` endpoint; a manual run creates an order and returns its ID.
- The dev connection string in `appsettings.Development.json`.

**NOT now:** inventory reservation, stock deduction, booking, concurrency, pricing rules beyond a
simple lookup, or any queries beyond the `GetAsync` needed to rebuild an order.

## Common mistakes to avoid

- **Business `if`s creeping into the handler.** The moment you write `if (status != Draft)` in a
  handler — stop. That belongs on `Order`.
- **A generic `Repository<T>`.** Forbidden here; it forces a lowest-common-denominator API and pushes
  you back toward dumb-data-bag domains.
- **Adding public setters to please Dapper.** Rebuild via the private constructor; keep the domain
  sealed.
- **Booking/approval logic sneaking into `usp_Order_Insert`.** That proc does persistence only.

---

## ✅ Check Yourself

1. **What's the difference between input validation and a business rule, and where does each live?**
2. **Trace a `POST /orders` request through every layer, naming what each one does.**
3. **Why does the handler contain no business `if`s, and where did they go?**
4. **Why is the transaction explicit in the handler instead of an automatic behavior — and what's the
   cost of that choice?**
5. **What belongs in `usp_Order_Insert`, and what deliberately does not?**
