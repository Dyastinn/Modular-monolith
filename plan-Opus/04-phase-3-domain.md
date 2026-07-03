# Phase 3 — Domain Model (DDD) + IDs

**Goal:** build the `SalesOrder` aggregate with real behavior and invariants, so later handlers
*orchestrate* the domain rather than containing the rules. Decide the ID strategy.
**Produces:** `SalesOrder` aggregate root, `SalesOrderLine`, value objects (`Money`, `Quantity`),
strongly-typed IDs, base `Entity`/`AggregateRoot`/`IDomainEvent` in `Shared.BuildingBlocks`, the domain
events, and the `SalesOrderStatus` state rules. Lives in `Modules/Sales/Domain`. No persistence yet.
**Prereq:** Phases 1–2 done.

> **Decision gate:** Tension B (generic `TId` vs strongly-typed IDs). This phase assumes strongly-typed
> IDs.

---

## Step 1 — The problem: the anemic domain

The reflex from WebForms/services is a `SalesOrder` that is just data:

```csharp
// ANEMIC — a bag of public setters, no rules.
public class SalesOrder
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public string Status { get; set; }        // "Draft"? "Submitted"? any string goes
    public List<SalesOrderLine> Lines { get; set; }
    public decimal Total { get; set; }         // who keeps this in sync?
}
```

with rules in a service (`if (o.Status != "Draft") ...; o.Status = "Submitted";`). **Why this rots:** the
invariants aren't protected by the `SalesOrder`; any code can set `Status = "Banana"` or forget to
recompute `Total`. Rules scatter and drift — the WebForms problem relocated to a "service layer" the brief
tells you to avoid.

## Step 2 — DDD building blocks, and the "why"

**Entity** — identity + lifecycle; equality by ID. **Value object** — defined by values, immutable
(`Money`, `Quantity`); enforces invariants a raw `decimal` can't. **Aggregate** — a consistency boundary
(`SalesOrder` + its lines). **Aggregate root** — the only entry point; single guardian of invariants.
**Domain event** — a raised fact (`SalesOrderSubmitted`), dispatched after commit, so side effects (even in
*another module*) aren't hard-wired in. **Domain service** — only for logic spanning aggregates (Phase 5).

Base types (`Entity`, `AggregateRoot`, `IDomainEvent`, `DomainException`) live in
`Erp.Shared.BuildingBlocks` (every module reuses them); the `SalesOrder` aggregate lives in
`Sales/Domain`.

## Step 3 — Strongly-typed IDs

```csharp
public readonly record struct SalesOrderId(long Value);
public readonly record struct CustomerId(long Value);
public readonly record struct ProductId(long Value);
```

- **Type safety:** `Submit(SalesOrderId id)` cannot take a `CustomerId`. A raw `long` would silently accept
  the wrong id.
- **Swap the backing type** in one line + one Dapper `TypeHandler` (Phase 4). No viral generic.

**Cost (honest):** each ID needs a `SqlMapper.TypeHandler<T>` (~10 lines), Phase 4.

## Step 4 — Base types (`Shared.BuildingBlocks`)

```csharp
public abstract class Entity<TId> where TId : struct { public TId Id { get; protected set; } }

public abstract class AggregateRoot<TId> : Entity<TId>
{
    private readonly List<IDomainEvent> _events = new();
    public IReadOnlyList<IDomainEvent> DomainEvents => _events;
    protected void Raise(IDomainEvent e) => _events.Add(e);
    public void ClearEvents() => _events.Clear();
}

public interface IDomainEvent { }
public sealed class DomainException(string message) : Exception(message);
```

`Entity<TId>` uses the *strongly-typed id* as its argument (`Entity<SalesOrderId>`) — the generic is
contained to the base class and disappears at the aggregate boundary.

## Step 5 — The `SalesOrder` aggregate (rich, invariant-protecting)

No public setters; `Total` derived; `SalesOrderStatus` an enum with a transition table; lines added through
the root; business methods raise events.

```csharp
namespace Erp.Modules.Sales.Domain;

public sealed class SalesOrder : AggregateRoot<SalesOrderId>
{
    private readonly List<SalesOrderLine> _lines = new();
    public IReadOnlyList<SalesOrderLine> Lines => _lines;
    public CustomerId CustomerId { get; private set; }
    public SalesOrderStatus Status { get; private set; }
    public Money Total => _lines.Aggregate(Money.Zero, (sum, l) => sum + l.LineTotal);

    private SalesOrder() { }  // rehydration by the repository (Phase 4)

    public static SalesOrder Create(CustomerId customerId)
    {
        var order = new SalesOrder { CustomerId = customerId, Status = SalesOrderStatus.Draft };
        order.Raise(new SalesOrderCreated(order.Id, customerId));
        return order;
    }

    public void AddLine(ProductId product, Quantity qty, Money unitPrice)
    {
        if (Status != SalesOrderStatus.Draft)
            throw new DomainException("Lines can only be added to a draft order.");
        _lines.Add(new SalesOrderLine(product, qty, unitPrice));
    }

    public void Submit(bool approvalGranted)
    {
        if (Status != SalesOrderStatus.Draft)
            throw new DomainException($"Only draft orders can be submitted (was {Status}).");
        if (_lines.Count == 0)
            throw new DomainException("Cannot submit an empty order.");
        if (RequiresApproval && !approvalGranted)
            throw new DomainException("Order exceeds approval threshold and needs manager approval.");
        Status = SalesOrderStatus.Submitted;
        Raise(new SalesOrderSubmitted(Id, CustomerId, Total));
    }

    public void Cancel()
    {
        if (!Status.CanTransitionTo(SalesOrderStatus.Cancelled))
            throw new DomainException($"Cannot cancel an order in status {Status}.");
        Status = SalesOrderStatus.Cancelled;
        Raise(new SalesOrderCancelled(Id));
    }

    public bool RequiresApproval => Total > ApprovalPolicy.Threshold;  // domain owns the rule
}
```

`Submit`/`Cancel` are the *only* ways status changes, each guarding its precondition. The handler in Phase
4/5 just calls `order.Submit(approvalGranted)` and re-checks nothing.

> **Out of the domain:** *how* it's stored (SQL), *who* called it (HTTP), and cross-aggregate atomicity
> (inventory reservation under concurrency — Phase 5's SQL guard, and it belongs to the *Inventory* module).

## Step 6 — `SalesOrderStatus` as a state machine

```csharp
public enum SalesOrderStatus { Draft, Submitted, Cancelled, Completed }
```

```
Draft     -> Submitted, Cancelled
Submitted -> Completed, Cancelled
Cancelled -> (terminal)
Completed -> (terminal)
```

Implement `CanTransitionTo` as a lookup so illegal transitions are impossible. Phase 7 exercises it; the
business tutorial adds an explicit manager `Approved` state on top.

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

Invalid states become unrepresentable — no negative price, no zero quantity.

> **Don't over-model.** `Money` is single-currency on purpose; add currency only when a requirement drives
> it.

## What to produce

- `Shared.BuildingBlocks`: `Entity<TId>`, `AggregateRoot<TId>`, `IDomainEvent`, `DomainException`.
- `Sales/Domain`: `SalesOrderId`/`CustomerId`/`ProductId`, `SalesOrder`, `SalesOrderLine`, `Money`,
  `Quantity`, `SalesOrderStatus` + transitions, `ApprovalPolicy`, events (`SalesOrderCreated`,
  `SalesOrderSubmitted`, `SalesOrderCancelled`).
- **Domain unit tests** in `Erp.Modules.Sales.UnitTests`.

**NOT in this phase:** repositories, Dapper, procs, handlers, HTTP.

## Pitfalls

- **Leaking setters** "for the mapper." Private ctor + `Create` factory; repository rehydrates via the
  private ctor.
- **Approval threshold in the handler.** It's a domain rule on `SalesOrder`.
- **`SalesOrder` in a shared project.** It belongs to the module; only the generic base types are shared.

---

## Checkpoint

1. Give a concrete bug an anemic `SalesOrder` allows and a rich one prevents.
2. Why strongly-typed ID structs, and what does it cost?
3. Why is `Total` computed, not a settable field?
4. Which rule lives on the entity, and why not the handler?
5. Why are base types shared but `SalesOrder` module-owned?
