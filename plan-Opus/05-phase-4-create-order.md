# Phase 4 — First Command Slice: CreateSalesOrder

**Goal:** wire one command end-to-end as **two files** so the anatomy of a slice is concrete: Carter
endpoint → validation behavior → handler → domain → repository → Dapper → stored procedure → transaction →
response. The template every later command copies.
**Produces:** the `CreateSalesOrder` slice (two files), `ValidationBehavior`, `IDbConnectionFactory`,
`ISalesOrderRepository` + Dapper impl, the ID `TypeHandler`s, `usp_SalesOrder_Insert`, explicit
transactions.
**Prereq:** Phases 1–3; a dev SQL Server database with the schema (§6).

---

## Step 1 — The anatomy of a command slice (two files)

```
Modules/Sales/Features/CreateSalesOrder/
    CreateSalesOrderHandler.cs    <- Command + Result + Validator + Handler
    CreateSalesOrderEndpoint.cs   <- Request + Response + Carter endpoint
Modules/Sales/Infrastructure/
    SalesOrderRepository.cs       <- ISalesOrderRepository impl (Dapper + stored procs)
    SalesOrderIdTypeHandler.cs
    Sql/usp_SalesOrder_Insert.sql
Shared/Erp.Shared.Infrastructure/Persistence/
    IDbConnectionFactory.cs, IUnitOfWork.cs (+ SQL Server impls)
```

Marker interfaces in `Shared.Messaging` (extend `IRequest<T>`): `ICommand<T>`, `IQuery<T>`,
`ICommandHandler<,>`, `IQueryHandler<,>`. **Do not split Command/Validator/Handler into separate files** —
keep the slice's behavior in one file, its web edge in the other.

```csharp
// CreateSalesOrderHandler.cs
public sealed record CreateSalesOrderCommand(long CustomerId, IReadOnlyList<OrderLineInput> Lines)
    : ICommand<CreateSalesOrderResult>;
public sealed record OrderLineInput(long ProductId, int Quantity);
public sealed record CreateSalesOrderResult(long SalesOrderId);

public sealed class CreateSalesOrderValidator : AbstractValidator<CreateSalesOrderCommand> { /* Step 2 */ }
public sealed class CreateSalesOrderHandler(...) : ICommandHandler<CreateSalesOrderCommand, CreateSalesOrderResult> { /* Step 4 */ }
```

```csharp
// CreateSalesOrderEndpoint.cs
public sealed record CreateSalesOrderRequest(long CustomerId, List<OrderLineInput> Lines);
public sealed record CreateSalesOrderResponse(long SalesOrderId);

public sealed class CreateSalesOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/sales-orders", async (CreateSalesOrderRequest r, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateSalesOrderCommand(r.CustomerId, r.Lines), ct);
            return Results.Created($"/sales-orders/{result.SalesOrderId}",
                new CreateSalesOrderResponse(result.SalesOrderId));
        });
}
```

Keep `Request` and `Command` separate types: `Request` = HTTP contract, `Command` = internal operation.
They diverge the moment the endpoint reads a value from the JWT rather than the body.

## Step 2 — Where validation belongs

| Kind | Question | Lives in | Example |
|---|---|---|---|
| **Input validation** | Well-formed? | **Validator** (handler file), run by `ValidationBehavior` before the handler | `CustomerId > 0`, ≥ 1 line, quantities present |
| **Business rule** | Allowed by the domain? | **Domain** (entity methods) | "only draft orders can be submitted", "customer must be active" |

```csharp
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
```

FluentValidation (MIT) is the idiomatic default. Unlike the mediator, validation isn't a teaching target —
use the library.

## Step 3 — The ValidationBehavior (in `Shared.Messaging`)

```csharp
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var failures = validators.Select(v => v.Validate(request)).SelectMany(r => r.Errors)
            .Where(f => f is not null).ToList();
        if (failures.Count != 0) throw new ValidationException(failures);
        return await next();
    }
}
```

Order: `Logging` outermost (logs even failed validation), then `Validation`, then the handler.

## Step 4 — The handler (orchestration only)

```csharp
public sealed class CreateSalesOrderHandler(
    ICustomerRepository customers, ISalesOrderRepository orders, IPricingService pricing, IUnitOfWork uow)
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

        await using var tx = await uow.BeginAsync(ct);   // explicit transaction boundary
        await orders.AddAsync(order, ct);
        await tx.CommitAsync(ct);

        return new CreateSalesOrderResult(order.Id.Value);
    }
}
```

Orchestrates (load, build, persist, commit); delegates rules (`EnsureCanOrder`, `AddLine`'s draft check,
`Quantity`'s positivity). No policy `if` here.

## Step 5 — Persistence with Dapper + stored procedure (module `Infrastructure`)

No generic repository. `ISalesOrderRepository` is specific to the aggregate and lives in
`Sales/Infrastructure/`:

```csharp
public interface ISalesOrderRepository
{
    Task AddAsync(SalesOrder order, CancellationToken ct);
    Task<SalesOrder?> GetAsync(SalesOrderId id, CancellationToken ct);   // rehydrates
    Task UpdateAsync(SalesOrder order, CancellationToken ct);            // Phase 5/7
}
```

Explicit persistence (no change tracker): pass the whole aggregate, write header + lines via
`usp_SalesOrder_Insert` / `usp_SalesOrderLine_Insert`. Register a Dapper `TypeHandler` per strongly-typed
ID. `AddSalesModule` registers the repos + type handlers — the Api never sees them.

## Step 6 — What goes in the stored procedure

```sql
CREATE PROCEDURE sales.usp_SalesOrder_Insert
    @CustomerId BIGINT, @Status INT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO sales.SalesOrder (CustomerId, Status, CreatedUtc)
    VALUES (@CustomerId, @Status, SYSUTCDATETIME());
    SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
END
```

Note the `sales.` schema — each module owns its schema. The schema enforces integrity (FKs, CHECK,
constraints); policy stays in the domain. No submit/approval logic here (split-brain, Tension A).

## Step 7 — Transactions (explicit, not a behavior)

Boundary visible in the handler (`uow.BeginAsync`/`CommitAsync`). Behavior-based transactions hide the
boundary and complicate event dispatch; explicit costs two lines and stays obvious. `IUnitOfWork`/
`IDbConnectionFactory` live in `Shared.Infrastructure`; the handler depends only on the interface.

## What to produce

- `ICommand<T>`/`IQuery<T>`/`ICommandHandler<,>` markers; the two `CreateSalesOrder` files.
- `ValidationBehavior<,>` registered in `Shared`; deliberate order vs `LoggingBehavior`.
- `IDbConnectionFactory`, `IUnitOfWork` + impls in `Shared.Infrastructure`.
- `ISalesOrderRepository` + impl (Dapper, procs) + ID `TypeHandler`s, registered in `AddSalesModule`.
- `ICustomerRepository` + impl with `Customer.EnsureCanOrder()`.
- SQL: `sales` schema for `SalesOrder`, `SalesOrderLine`, `Customer`, `Product` + insert/get procs.
- `POST /sales-orders` works end to end; Development connection string.

**NOT in this phase:** inventory reservation, the second module, submit/approval, concurrency, pricing rules
beyond a lookup, queries beyond `GetAsync`.

## Pitfalls

- **Four files instead of two.** Keep behavior in one file, endpoint in one.
- **Business `if`s in the handler.** `if (status != Draft)` belongs on `SalesOrder`.
- **A generic `Repository<T>`.** Forbidden.
- **Public setters for Dapper.** Rehydrate via the private ctor.
- **Submit/approval logic in the proc.** Persistence only.

---

## Checkpoint

1. What two files make up a slice, what's in each, and why not four?
2. Input validation vs. business rule — where does each live?
3. Trace `POST /sales-orders` through every step.
4. Why no policy `if`s in the handler, and where did they go?
5. What belongs in `usp_SalesOrder_Insert` and what does not?
