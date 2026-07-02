# Phase 4 — First Command Slice: CreateOrder

**Goal:** wire one command end-to-end so the anatomy of a slice is concrete: endpoint → validation
behavior → handler → domain → repository → Dapper → stored procedure → transaction → response. This
is the template every later command copies.
**Produces:** the `CreateOrder` slice, `ValidationBehavior`, `IDbConnectionFactory`, `IOrderRepository`
+ Dapper implementation, the ID `TypeHandler`s, the `usp_Order_Insert` stored procedure, and explicit
transaction handling.
**Prereq:** Phases 1–3 done; a dev SQL Server database with the schema (see §6).

---

## Step 1 — The anatomy of a command slice

```
Application/Orders/CreateOrder/
    CreateOrderCommand.cs     <- inputs (a record); implements ICommand<OrderId>
    CreateOrderValidator.cs   <- input validation (well-formedness), NOT business rules
    CreateOrderHandler.cs     <- orchestration: load deps, run domain, persist, return
Api/Endpoints/Orders/
    CreateOrderEndpoint.cs    <- POST /orders -> ISender.Send(command)
Infrastructure/Persistence/Orders/
    OrderRepository.cs        <- IOrderRepository impl (Dapper + stored procs)
db/
    usp_Order_Insert.sql
```

Add thin marker interfaces to make intent visible (they just extend `IRequest<T>`):

```csharp
public interface ICommand<TResponse> : IRequest<TResponse> { }
public interface IQuery<TResponse>   : IRequest<TResponse> { }
```

`CreateOrder` is a **command** (it changes state) returning the new `OrderId`.

## Step 2 — Where validation belongs (a core learning goal)

There are **two** kinds of "validation" and they live in different places:

| Kind | Question | Lives in | Example |
|---|---|---|---|
| **Input validation** | Is the request well-formed? | **Validator** (Application slice), run by `ValidationBehavior` before the handler | `CustomerId > 0`, at least one line, quantities present |
| **Business rule** | Is this allowed by the domain? | **Domain** (entity methods) | "only draft orders can be booked", "customer must be active" |

Do **not** put business rules in the validator. The validator answers "should this request even reach
the handler?" The domain answers "is this operation legal given the state of the world?" Mixing them
scatters rules and makes the domain non-authoritative again.

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

> **Validation library:** FluentValidation is MIT-licensed and free (verify the current package
> license before adding). If you'd rather hand-roll to avoid any dependency, a validator is just a
> method returning a list of errors — the `ValidationBehavior` doesn't care how validation is
> implemented. Either is fine; FluentValidation is the idiomatic default.

## Step 3 — The ValidationBehavior (why it's a pipeline behavior)

Instead of every handler calling its validator, one behavior does it for all commands, before the
handler runs. This is the concrete payoff of Phase 2's pipeline:

```csharp
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var failures = validators
            .Select(v => v.Validate(request))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();
        if (failures.Count != 0) throw new ValidationException(failures);
        return await next();
    }
}
```

Register it **before** `LoggingBehavior`? No — decide the order deliberately: typically
`Logging` outermost (so you log even failed validation), then `Validation`, then the handler. Register
in that outer-to-inner order.

## Step 4 — The handler (orchestration only)

The handler coordinates; it holds **no business rules** — those are in the domain.

```csharp
public sealed class CreateOrderHandler(
    ICustomerRepository customers,
    IOrderRepository orders,
    IPricingService pricing,          // introduced properly in Phase 5; here: unit price lookup
    IUnitOfWork uow)
    : IRequestHandler<CreateOrderCommand, OrderId>
{
    public async Task<OrderId> Handle(CreateOrderCommand cmd, CancellationToken ct)
    {
        var customerId = new CustomerId(cmd.CustomerId);

        // Business rule lives in the domain / a check the domain owns — handler just invokes it.
        var customer = await customers.GetAsync(customerId, ct)
            ?? throw new NotFoundException($"Customer {cmd.CustomerId} not found.");
        customer.EnsureCanOrder();          // domain method: throws if on credit hold / inactive

        var order = Order.Create(customerId);
        foreach (var line in cmd.Lines)
        {
            var unitPrice = await pricing.GetUnitPrice(new MaterialId(line.MaterialId), ct);
            order.AddLine(new MaterialId(line.MaterialId), new Quantity(line.Quantity), unitPrice);
        }

        await using var tx = await uow.BeginAsync(ct);   // explicit transaction boundary
        await orders.AddAsync(order, ct);
        await tx.CommitAsync(ct);

        // Domain events dispatched after commit — full treatment in Phase 7.
        return order.Id;
    }
}
```

Read what the handler does and does **not** do: it *orchestrates* (load customer, build order, persist,
commit) and *delegates rules* (`EnsureCanOrder`, `AddLine`'s draft check, `Quantity`'s positivity). No
`if` about business policy lives here. If you find a policy `if` creeping into a handler, it belongs in
the domain.

## Step 5 — Persistence with Dapper + stored procedure

**No generic repository.** `IOrderRepository` is specific to the `Order` aggregate:

```csharp
public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken ct);
    Task<Order?> GetAsync(OrderId id, CancellationToken ct);   // rehydrates the aggregate
    Task UpdateAsync(Order order, CancellationToken ct);       // Phase 5/7
}
```

The Dapper implementation calls stored procedures and maps rows back into the aggregate. Because there's
no change tracker, persistence is **explicit**: you pass the whole aggregate and the repository writes
header + lines.

```csharp
public sealed class OrderRepository(IDbConnectionFactory factory) : IOrderRepository
{
    public async Task AddAsync(Order order, CancellationToken ct)
    {
        using var conn = factory.Create();
        // The repository maps the aggregate to proc params. It does NOT contain business rules.
        var orderId = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "usp_Order_Insert",
            new { order.CustomerId /* mapped via TypeHandler */, Status = (int)order.Status },
            commandType: CommandType.StoredProcedure, cancellationToken: ct));

        foreach (var line in order.Lines)
            await conn.ExecuteAsync(new CommandDefinition(
                "usp_OrderLine_Insert",
                new { OrderId = orderId, line.MaterialId, Quantity = line.Quantity.Value,
                      UnitPrice = line.UnitPrice.Amount },
                commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }
    // GetAsync: call usp_Order_Get + usp_OrderLine_GetByOrder, rehydrate via the private ctor.
}
```

**Strongly-typed ID mapping** — register a Dapper `TypeHandler` per ID so `OrderId`/`CustomerId` map
to/from the column type. This is the ~10-lines-per-ID cost noted in Phase 3:

```csharp
public sealed class OrderIdTypeHandler : SqlMapper.TypeHandler<OrderId>
{
    public override OrderId Parse(object value) => new((long)value);
    public override void SetValue(IDbDataParameter p, OrderId value) => p.Value = value.Value;
}
// SqlMapper.AddTypeHandler(new OrderIdTypeHandler()); at startup.
```

## Step 6 — What goes in the stored procedure (and what doesn't)

`usp_Order_Insert` does **persistence and integrity**, not policy:

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

The **schema** enforces integrity the app shouldn't have to trust: `CustomerId` is a FK, `Status` is
constrained (CHECK or a lookup table), quantities are `> 0` via CHECK as a backstop. Integrity in SQL;
policy ("only draft can be booked") in the domain. No booking/approval logic here — that would be the
split-brain Tension A warns about.

## Step 7 — Transactions (why explicit, not a behavior)

We keep the transaction boundary **visible in the handler** (`uow.BeginAsync` / `CommitAsync`) rather
than hiding it in a `TransactionBehavior`. Tradeoff, stated:

- **Behavior-based transactions** (wrap every command in a transaction automatically) are convenient
  and DRY, but they hide *where* the boundary is, wrap queries you didn't mean to, and complicate
  domain-event dispatch ordering.
- **Explicit transactions** cost a couple of lines per command but make the boundary obvious while
  you're learning where it belongs. For CreateOrder the boundary spans "insert header + all lines."

`IUnitOfWork`/`IDbConnectionFactory` live in Infrastructure; the handler depends only on the
`IUnitOfWork` interface (declared in Application/Domain), preserving the dependency rule.

> Once the pattern is second nature, promoting transactions to a command-only behavior is a reasonable
> refactor — but do it *after* you've felt where the boundary goes, not before.

## What Sonnet should produce

- `ICommand<T>`/`IQuery<T>` markers; `CreateOrderCommand`, `CreateOrderValidator`, `CreateOrderHandler`.
- `ValidationBehavior<,>` registered in deliberate order relative to `LoggingBehavior`.
- `IDbConnectionFactory`, `IUnitOfWork` (interfaces) + SQL Server implementations.
- `IOrderRepository` + `OrderRepository` (Dapper, stored procs), ID `TypeHandler`s registered.
- `ICustomerRepository` + impl with `Customer.EnsureCanOrder()` on the domain side (minimal Customer
  aggregate is fine here; expand in Phase 5 as needed).
- SQL: schema for `Order`, `OrderLine`, `Customer`, `Material` (minimal), plus `usp_Order_Insert`,
  `usp_OrderLine_Insert`, `usp_Order_Get`, `usp_OrderLine_GetByOrder`.
- `POST /orders` endpoint; a manual run creates an order and returns its id.
- Development connection string in `appsettings.Development.json` (Testing string comes in Phase 8).

**NOT in this phase:** inventory reservation, stock deduction, booking, concurrency, pricing rules
(beyond a simple unit-price lookup), queries beyond `GetAsync` for rehydration.

## Pitfalls

- **Business `if`s migrating into the handler.** The moment you write `if (status != Draft)` in the
  handler, stop — that belongs on `Order`.
- **A generic `Repository<T>`.** Forbidden by the brief and by good sense here; it forces a
  lowest-common-denominator API and encourages anemic aggregates.
- **Public setters to satisfy Dapper.** Rehydrate via the private ctor; keep the domain sealed.
- **Booking/approval logic sneaking into `usp_Order_Insert`.** Persistence only.

---

## Checkpoint — you understand this phase when you can answer:

1. What's the difference between input validation and a business rule, and where does each live?
2. Trace a `POST /orders` request through every layer, naming what each does.
3. Why does the handler contain no policy `if`s, and where did they go?
4. Why is the transaction explicit in the handler instead of a pipeline behavior — and what's the cost?
5. What belongs in `usp_Order_Insert` and what deliberately does not?
