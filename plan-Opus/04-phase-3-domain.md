# Phase 3 — Domain Model (DDD) + IDs

**Goal:** build the `Order` aggregate with real behavior and invariants, so later handlers *orchestrate*
the domain rather than containing the rules. Decide the ID strategy.
**Produces:** `Order` aggregate root, `OrderLine`, value objects (`Money`, `Quantity`), strongly-typed
IDs, base `Entity`/`AggregateRoot`, `IDomainEvent` and the events, and the `OrderStatus` state rules.
No persistence yet.
**Prereq:** Phases 1–2 done.

> **Decision gate:** Tension B in the overview (generic `TId` vs strongly-typed IDs). This phase assumes
> the recommended **strongly-typed IDs**. If you're overriding to literal `TId`, read §2 and the
> override note before building.

---

## Step 1 — The problem: the anemic domain

The reflex coming from WebForms/services is an `Order` that is just data:

```csharp
// ANEMIC — a bag of public setters, no rules.
public class Order
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public string Status { get; set; }        // "Draft"? "Booked"? any string goes
    public List<OrderLine> Lines { get; set; }
    public decimal Total { get; set; }         // who keeps this in sync?
}
```

Then the rules live in a service:

```csharp
public class OrderService
{
    public void Book(Order o)
    {
        if (o.Status != "Draft") throw ...;    // rule lives here
        if (o.Total > 50000 && !approved) ...;  // and here
        o.Status = "Booked";                     // anyone can also just set this elsewhere
    }
}
```

**Why this rots:** the invariants (`Total` matches the lines; only `Draft` can be booked; status is a
known value) are not protected by the `Order`. Any code can set `Status = "Banana"` or forget to
recompute `Total`. The rules scatter into whatever service touches orders, and drift — the exact
WebForms problem, relocated to a service layer. This is *"a generic service layer"* the brief tells
you to avoid, and now you can see why.

## Step 2 — The DDD building blocks, and the "why" of each

**Entity** — has identity and a lifecycle; equality is by ID. `Order`, `Customer`.
**Value object** — defined by its values; immutable; equality by value. `Money`, `Quantity`. Exists
because `decimal amount` everywhere loses meaning and invariants (a `Money` can enforce non-negative,
carry a currency, and refuse to add two different currencies — a raw `decimal` can't).
**Aggregate** — a consistency boundary. `Order` + its `OrderLine`s change together and must stay
consistent as a unit.
**Aggregate root** — the only entry point. Outside code holds an `Order`, never an `OrderLine`
directly. Exists so invariants have a single guardian: all mutation goes through the root's methods.
**Domain event** — a fact ("`OrderBooked`") the aggregate raises, dispatched after commit. Exists so
side effects (notify, decrement a read model, integrate) don't get hard-wired into the domain.
**Domain service** — *only* for logic spanning multiple aggregates with no natural home on one entity.
Deferred to Phase 5; most rules belong on the entity, not a service.

## Step 3 — Strongly-typed IDs (the ID decision)

```csharp
public readonly record struct OrderId(long Value);
public readonly record struct CustomerId(long Value);
public readonly record struct MaterialId(long Value);
```

- **Type safety:** `Book(OrderId id)` cannot be called with a `CustomerId` — the compiler stops it.
  A raw `long` would silently accept the wrong id. This kills a real, common bug class.
- **Swap the backing type:** to move `OrderId` from `long` to `Guid`/`ulong`/ULID, change `long` here
  and the corresponding Dapper `TypeHandler` (Phase 4). No viral generic parameter across the codebase.

**Cost (honest):** each ID needs a Dapper `SqlMapper.TypeHandler<OrderId>` (~10 lines) mapping to/from
the column type, added in Phase 4. That's the price of the type safety.

> **Override note (literal generic `TId`):** if you insist on the brief's `TId`, you'd make `Entity<TId>`
> and thread `TId` through repositories and handlers. It works, but every generic signature carries the
> parameter and you lose the "can't mix an OrderId and CustomerId" safety unless you *also* wrap them —
> at which point you've done more work than strongly-typed IDs for a worse result. Recorded here so the
> tradeoff is explicit; the default remains strongly-typed IDs.

## Step 4 — The base types

```csharp
public abstract class Entity<TId> where TId : struct   // TId is the strongly-typed id struct
{
    public TId Id { get; protected set; }
}

public abstract class AggregateRoot<TId> : Entity<TId>
{
    private readonly List<IDomainEvent> _events = new();
    public IReadOnlyList<IDomainEvent> DomainEvents => _events;
    protected void Raise(IDomainEvent e) => _events.Add(e);
    public void ClearEvents() => _events.Clear();
}

public interface IDomainEvent { }   // marker; concrete events are records
```

Note `Entity<TId>` uses the *strongly-typed id* as its type argument (`Entity<OrderId>`), which is
different from a generic `TId` threaded through the whole app — the generic here is contained to the
base class and disappears at the aggregate boundary.

## Step 5 — The `Order` aggregate (rich, invariant-protecting)

Key design points:

- **No public setters.** State changes only through methods that enforce rules.
- **`Total` is derived**, never stored publicly — it's computed from lines, so it can't drift.
- **`OrderStatus` is an enum with an explicit transition table** (see §6), not a free string.
- **Line items are added through the root**, which keeps the aggregate consistent.
- **Business methods raise domain events.**

```csharp
public sealed class Order : AggregateRoot<OrderId>
{
    private readonly List<OrderLine> _lines = new();
    public IReadOnlyList<OrderLine> Lines => _lines;
    public CustomerId CustomerId { get; private set; }
    public OrderStatus Status { get; private set; }
    public Money Total => _lines.Aggregate(Money.Zero, (sum, l) => sum + l.LineTotal);

    private Order() { }  // for rehydration by the repository (Phase 4)

    public static Order Create(CustomerId customerId)
    {
        var order = new Order { CustomerId = customerId, Status = OrderStatus.Draft };
        order.Raise(new OrderCreated(order.Id, customerId));
        return order;
    }

    public void AddLine(MaterialId material, Quantity qty, Money unitPrice)
    {
        if (Status != OrderStatus.Draft)
            throw new DomainException("Lines can only be added to a draft order.");
        _lines.Add(new OrderLine(material, qty, unitPrice));
    }

    public void Book(bool approvalGranted)
    {
        if (Status != OrderStatus.Draft)
            throw new DomainException($"Only draft orders can be booked (was {Status}).");
        if (_lines.Count == 0)
            throw new DomainException("Cannot book an empty order.");
        if (RequiresApproval && !approvalGranted)
            throw new DomainException("Order exceeds approval threshold and needs manager approval.");
        Status = OrderStatus.Booked;
        Raise(new OrderBooked(Id, CustomerId, Total));
    }

    public void Cancel()
    {
        if (!Status.CanTransitionTo(OrderStatus.Cancelled))
            throw new DomainException($"Cannot cancel an order in status {Status}.");
        Status = OrderStatus.Cancelled;
        Raise(new OrderCancelled(Id));
    }

    public bool RequiresApproval => Total > ApprovalPolicy.Threshold;  // domain owns the rule
}
```

Notice: `Book` and `Cancel` are the *only* ways status changes, and each guards its precondition. The
approval threshold is a **domain rule**, not an `if` in a handler. This is the payoff — the handler in
Phase 4/5 will just call `order.Book(approvalGranted)` and won't re-check any of this.

> **What stays OUT of the domain:** anything about *how* it's stored (SQL), *who* called it (HTTP),
> and cross-aggregate atomicity (inventory reservation under concurrency — that's Phase 5's SQL guard).
> The domain expresses "reserve only if available"; it does not, by itself, make that safe under
> concurrent callers. Keep that boundary sharp.

## Step 6 — `OrderStatus` as a state machine

```csharp
public enum OrderStatus { Draft, Booked, Cancelled, Completed }
```

Legal transitions (a real business rule — encode it, don't leave it implicit):

```
Draft     -> Booked, Cancelled
Booked    -> Completed, Cancelled
Cancelled -> (terminal)
Completed -> (terminal)
```

Implement `CanTransitionTo` as a small lookup so illegal transitions are impossible, not merely
discouraged. Phase 7 exercises the full state machine.

## Step 7 — Value objects

```csharp
public readonly record struct Quantity
{
    public int Value { get; }
    public Quantity(int value) =>
        Value = value > 0 ? value : throw new DomainException("Quantity must be positive.");
}

public readonly record struct Money
{
    public decimal Amount { get; }
    public static readonly Money Zero = new(0m);
    public Money(decimal amount) =>
        Amount = amount >= 0 ? amount : throw new DomainException("Money cannot be negative.");
    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount);
    public static Money operator *(Money m, int q) => new(m.Amount * q);
    public static bool operator >(Money a, Money b) => a.Amount > b.Amount;
    public static bool operator <(Money a, Money b) => a.Amount < b.Amount;
}
```

`Money`/`Quantity` make invalid states unrepresentable — you cannot construct a negative price or a
zero quantity. That's the value-object payoff over raw `decimal`/`int`.

> **Don't over-model.** `Money` here is single-currency on purpose (this ERP is one currency). Adding
> currency now would be abstraction without a driving requirement — exactly what the brief says to
> avoid. If multi-currency becomes real, `Money` is the one place it changes.

## What Sonnet should produce

- `Common`: `Entity<TId>`, `AggregateRoot<TId>`, `IDomainEvent`, `DomainException`.
- `Orders` domain: `OrderId`/`CustomerId`/`MaterialId`, `Order`, `OrderLine`, `Money`, `Quantity`,
  `OrderStatus` + transition rules, `ApprovalPolicy` (threshold constant/config), domain events
  (`OrderCreated`, `OrderBooked`, `OrderCancelled`).
- **Unit tests for the domain** (yes, in this phase — they're free and prove the rules): booking a
  non-draft throws; empty order can't be booked; over-threshold without approval throws; illegal
  status transitions throw; `Money`/`Quantity` reject invalid values.

**NOT in this phase:** repositories, Dapper, stored procs, handlers, HTTP. The domain compiles and is
tested in isolation with zero infrastructure — that isolation is the whole point.

## Pitfalls

- **Leaking setters** "for the ORM/mapper." Use a private ctor + factory (`Create`) and let the
  repository rehydrate via that private ctor / reflection in Phase 4. Public setters reopen the
  anemic-domain hole.
- **Putting the approval threshold in the handler.** It's a domain rule; it lives on `Order`.
- **Modeling things the brief has no requirement for** (currencies, tax engines). Stop at the rules
  the twelve operations actually need.

---

## Checkpoint — you understand this phase when you can answer:

1. Give a concrete bug that an anemic `Order` allows and a rich `Order` prevents.
2. Why are IDs strongly-typed structs instead of `long`, and what does that cost?
3. Why is `Total` a computed property and not a settable field?
4. Which order rule lives on the entity, and why not in the handler?
5. Name one thing that deliberately does *not* go in the domain, and where it goes instead.
