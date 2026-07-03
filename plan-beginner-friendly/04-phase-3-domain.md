# Phase 3 — The Smart `Order` Object (the heart of the project)

> 📄 Expert version: [`../plan-Opus/04-phase-3-domain.md`](../plan-Opus/04-phase-3-domain.md)

**In one sentence:** We build an `Order` object that *knows and protects its own rules*, so later code
just *asks* it to do things instead of policing it from outside.

**What you'll build:** The `Order` object (with real behavior), its line items, small value objects
like `Money` and `Quantity`, strongly-typed IDs, and the rules for how an order's status can change. No
database yet — this is pure C#.

**Before you start:** Phases 1–2 done.

---

## Step 1 — The trap: the "dumb data bag" (anemic) `Order`

Coming from most backgrounds, the natural first instinct is an `Order` that's just data with public
setters:

```csharp
// ❌ ANEMIC — a bag of properties anyone can set to anything.
public class Order
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public string Status { get; set; }     // "Draft"? "Booked"? "Banana"? Anything goes!
    public List<OrderLine> Lines { get; set; }
    public decimal Total { get; set; }     // who keeps this correct when lines change?
}
```

Then the rules live somewhere *else*, in a service:

```csharp
public class OrderService
{
    public void Book(Order o)
    {
        if (o.Status != "Draft") throw ...;    // rule lives here...
        if (o.Total > 50000 && !approved) ...;  // ...and here...
        o.Status = "Booked";                     // ...but anyone can set Status elsewhere too!
    }
}
```

**Why this rots:** the `Order` doesn't protect anything. Any code anywhere can write
`order.Status = "Banana"`, or add a line and forget to update `Total`. The rules get scattered across
every service that touches orders, and they drift apart. **This is the exact WebForms problem from
Phase 0, just relocated to a "service."** It's precisely the "generic service layer" we're told to
avoid — and now you can see *why* it's bad.

## Step 2 — The building blocks (and why each exists)

Domain-Driven Design gives us a small vocabulary of object types. Here's each, with its reason for
existing:

- **Entity** — has an identity and a lifespan. Two orders with identical details are still *different
  orders*. Examples: `Order`, `Customer`. *(Two people named John Smith are still two people.)*
- **Value object** — defined purely by its values, and never changes after creation. Examples: `Money`,
  `Quantity`. Why bother? Because a raw `decimal` can't protect itself — but a `Money` type can *refuse*
  to be negative, and a `Quantity` can *refuse* to be zero. *(Any $10 bill equals any other $10 bill.)*
- **Aggregate** — a group of objects that must stay consistent together. `Order` + its `OrderLine`s
  are one aggregate; they change as a unit.
- **Aggregate root** — the single front door to the aggregate. Outside code holds an `Order`, never
  grabs an `OrderLine` directly. This gives the rules *one guardian*: everything goes through the
  root's methods. *(You talk to the store manager, not the stock in the back room.)*
- **Domain event** — a recorded fact that something happened ("OrderBooked"). Lets other code react
  later without hard-wiring side effects into the `Order`.
- **Domain service** — reserved for logic that spans *several* aggregates and has no natural home on
  one object. Rare — we don't need one until Phase 5.

## Step 3 — Strongly-typed IDs (a small idea with a big payoff)

Instead of using a plain `long` for every ID, we make a distinct little type for each:

```csharp
public readonly record struct OrderId(long Value);
public readonly record struct CustomerId(long Value);
public readonly record struct MaterialId(long Value);
```

Why go to this trouble?

- **The compiler catches ID mix-ups.** A method `Book(OrderId id)` *cannot* be called with a
  `CustomerId` — the code won't compile. With a plain `long`, you could pass the wrong ID and it'd
  silently "work" (wrongly). This kills a whole category of real, nasty bugs. *(Color-coded keys: the
  red key physically won't fit the blue lock.)*
- **You can swap the underlying type easily.** Want `OrderId` to be a `Guid` instead of a `long` later?
  Change it in *one line* here (plus one small database-mapping helper in Phase 4). No ripple across
  the whole codebase.

> 💬 **Honest cost:** each ID type needs a ~10-line "type handler" so Dapper knows how to read/write it
> to the database (we add those in Phase 4). That's the price of the safety — and it's worth it.

## Step 4 — The base building blocks

A couple of small base classes that all aggregates share:

```csharp
public abstract class Entity<TId> where TId : struct   // TId is the strongly-typed ID
{
    public TId Id { get; protected set; }
}

public abstract class AggregateRoot<TId> : Entity<TId>
{
    private readonly List<IDomainEvent> _events = new();
    public IReadOnlyList<IDomainEvent> DomainEvents => _events;
    protected void Raise(IDomainEvent e) => _events.Add(e);   // record "something happened"
    public void ClearEvents() => _events.Clear();
}

public interface IDomainEvent { }   // a marker; the actual events are records
```

## Step 5 — The rich `Order` (this is the payoff)

Now the good version. Notice the design choices:

- **No public setters.** State only changes through methods that enforce rules.
- **`Total` is *calculated*** from the lines every time — it can't drift out of sync, because it's
  never stored separately.
- **`Status` is a real enum with allowed transitions**, not a free-for-all string.
- **Lines are added through the root**, keeping the aggregate consistent.
- **Business methods record domain events.**

```csharp
public sealed class Order : AggregateRoot<OrderId>
{
    private readonly List<OrderLine> _lines = new();
    public IReadOnlyList<OrderLine> Lines => _lines;
    public CustomerId CustomerId { get; private set; }
    public OrderStatus Status { get; private set; }

    // Total is COMPUTED from the lines — it literally cannot be wrong.
    public Money Total => _lines.Aggregate(Money.Zero, (sum, l) => sum + l.LineTotal);

    private Order() { }  // private — only the repository uses this to rebuild from the DB (Phase 4)

    // The only way to make a new Order — and it starts as Draft.
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

    // The approval threshold is a BUSINESS RULE, so it lives here — not in a handler.
    public bool RequiresApproval => Total > ApprovalPolicy.Threshold;
}
```

Read what this buys you: `Book` and `Cancel` are the *only* ways `Status` can change, and each one
checks its own preconditions first. The $50,000 approval rule is a property *on the order*, not an `if`
buried in some handler. **The payoff:** the handler in Phase 4/5 will just call `order.Book(approved)`
and won't re-check *any* of this — because the `Order` already guarantees it.

> 🚫 **What deliberately stays OUT of the `Order`:** how it's saved (SQL), who called it (the web),
> and safety-under-simultaneous-access (that's Phase 5's database guard). The `Order` can *express* the
> rule "reserve only if available," but it cannot, by itself, make that safe when two people order at
> once. Keep that line sharp — it's the theme of Phase 5.

## Step 6 — `OrderStatus` as a state machine

An order's status can't jump around arbitrarily. Encode the legal moves:

```csharp
public enum OrderStatus { Draft, Booked, Cancelled, Completed }
```

Allowed transitions (a real business rule — write it down, don't leave it implied):

```
Draft     → Booked, Cancelled
Booked    → Completed, Cancelled
Cancelled → (nowhere — it's final)
Completed → (nowhere — it's final)
```

We implement a small `CanTransitionTo` lookup so illegal moves are *impossible*, not merely frowned
upon. *(Analogy: a board game where the pieces physically can't move to illegal squares.)* Phase 7
exercises the full machine.

## Step 7 — Value objects: making bad values impossible

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

The point: you **cannot construct** a negative price or a zero quantity — the object refuses. Invalid
states become *unrepresentable*. A raw `decimal`/`int` can't protect you like that.

> 🚫 **Don't over-model.** `Money` here is single-currency on purpose — this pretend company uses one
> currency. Adding multi-currency now would be building for a requirement that doesn't exist (exactly
> the over-engineering we avoid). If it ever becomes real, `Money` is the *one* place it changes.

## What to produce in this phase

- `Common`: `Entity<TId>`, `AggregateRoot<TId>`, `IDomainEvent`, `DomainException`.
- `Orders`: the IDs, `Order`, `OrderLine`, `Money`, `Quantity`, `OrderStatus` + transition rules,
  `ApprovalPolicy` (the threshold), and the domain events (`OrderCreated`, `OrderBooked`,
  `OrderCancelled`).
- **Unit tests for the domain** (yes, now — they're basically free and prove the rules work): booking a
  non-draft throws; an empty order can't be booked; over-threshold without approval throws; illegal
  status transitions throw; `Money`/`Quantity` reject bad values.

**NOT now:** no repositories, no database, no handlers, no web. The domain compiles and is tested with
*zero* infrastructure — that isolation is the whole point.

## Common mistakes to avoid

- **Adding public setters "so the database mapper can fill it in."** Don't — use the private constructor
  + `Create` factory, and let the repository rebuild via that private constructor in Phase 4. Public
  setters re-open the dumb-data-bag hole.
- **Putting the approval threshold in a handler.** It's a business rule; it lives on `Order`.
- **Modeling things nothing requires yet** (currencies, tax engines). Stop at the rules the actual
  operations need.

---

## ✅ Check Yourself

1. **Give a concrete bug the dumb (anemic) `Order` allows that the smart (rich) `Order` prevents.**
   *(Hint: `Status = "Banana"`, or a `Total` that doesn't match the lines.)*
2. **Why are IDs strongly-typed structs instead of `long`, and what does that cost?**
3. **Why is `Total` a calculated property instead of a stored field you can set?**
4. **Which order rule lives on the entity, and why not in the handler?** *(The approval threshold.)*
5. **Name one thing that deliberately does *not* go in the domain, and where it goes instead.**
