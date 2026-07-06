# Phase 3 — The Smart `SalesOrder` Object (the heart of the project)

> 📄 Expert version: [`../plan-Opus/04-phase-3-domain.md`](../plan-Opus/04-phase-3-domain.md)

**In one sentence:** We build a `SalesOrder` object that *knows and protects its own rules*, so later code
just *asks* it to do things instead of policing it from outside.

**What you'll build:** The `SalesOrder` object (with real behavior), its line items, small value objects
like `Money` and `Quantity`, strongly-typed IDs, and the rules for how a sales order's status can change.
No database yet — this is pure C#. It lives in the module's `Sales/Domain/` folder; the shared base types
live in `Erp.Shared.BuildingBlocks`.

**Before you start:** Phases 1–2 done.

---

## Step 1 — The trap: the "dumb data bag" (anemic) `SalesOrder`

Coming from most backgrounds — and *especially* from WebForms, where the "order" was just a DataSet or a
bag of properties — the natural instinct is a `SalesOrder` that's just data with public setters:

```csharp
// ❌ ANEMIC — a bag of properties anyone can set to anything.
public class SalesOrder
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public string Status { get; set; }     // "Draft"? "Submitted"? "Banana"? Anything goes!
    public List<SalesOrderLine> Lines { get; set; }
    public decimal Total { get; set; }     // who keeps this correct when lines change?
}
```

Then the rules live somewhere *else*, in a service:

```csharp
public class SalesOrderService
{
    public void Submit(SalesOrder o)
    {
        if (o.Status != "Draft") throw ...;      // rule lives here...
        if (o.Total > 50000 && !approved) ...;   // ...and here...
        o.Status = "Submitted";                   // ...but anyone can set Status elsewhere too!
    }
}
```

**Why this rots:** the `SalesOrder` doesn't protect anything. Any code anywhere can write
`order.Status = "Banana"`, or add a line and forget to update `Total`. The rules scatter across every
service that touches orders, and they drift apart. **This is the exact WebForms problem from Phase 0, just
relocated to a "service."** Now you can see *why* a generic "service layer" is a trap.

## Step 2 — The building blocks (and why each exists)

A rich domain model gives us a small vocabulary of object types. Each, with its reason for existing:

- **Entity** — has an identity and a lifespan. Two orders with identical details are still *different
  orders*. Examples: `SalesOrder`, `Customer`. *(Two people named John Smith are still two people.)*
- **Value object** — defined purely by its values, and never changes after creation. Examples: `Money`,
  `Quantity`. Why bother? A raw `decimal` can't protect itself — but a `Money` type can *refuse* to be
  negative, and a `Quantity` can *refuse* to be zero. *(Any ₱10 bill equals any other ₱10 bill.)*
- **Aggregate** — a group of objects that must stay consistent together. `SalesOrder` + its
  `SalesOrderLine`s are one aggregate; they change as a unit.
- **Aggregate root** — the single front door to the aggregate. Outside code holds a `SalesOrder`, never
  grabs a `SalesOrderLine` directly. One guardian: everything goes through the root's methods. *(You talk
  to the store manager, not the stock in the back room.)*
- **Domain event** — a recorded fact that something happened (`SalesOrderSubmitted`). Lets other code — or
  even *another module* — react later without hard-wiring side effects into the `SalesOrder`.
- **Domain service** — reserved for logic that spans *several* aggregates and has no natural home on one
  object. Rare — we don't need one until Phase 5.

These base types (`Entity`, `AggregateRoot`, `IDomainEvent`) live in **`Erp.Shared.BuildingBlocks`**, since
*every* module's domain reuses them. The `SalesOrder` itself lives in **`Sales/Domain/`** — it's this
module's business object, not shared.

## Step 3 — Strongly-typed IDs (a small idea with a big payoff)

Instead of a plain `long` for every ID, we make a distinct little type for each:

```csharp
public readonly record struct SalesOrderId(long Value);
public readonly record struct CustomerId(long Value);
public readonly record struct ProductId(long Value);
```

Why go to this trouble?

- **The compiler catches ID mix-ups.** A method `Submit(SalesOrderId id)` *cannot* be called with a
  `CustomerId` — the code won't compile. With a plain `long`, you could pass the wrong ID and it'd silently
  "work" (wrongly). This kills a whole category of real, nasty bugs. *(Color-coded keys: the red key won't
  fit the blue lock.)*
- **You can swap the underlying type easily.** Want `SalesOrderId` to be a `Guid` later? Change it in *one
  line* here (plus one small database-mapping helper in Phase 4).

> 💬 **Honest cost:** each ID type needs a ~10-line "type handler" so Dapper knows how to read/write it to
> the database (we add those in Phase 4). That's the price of the safety — and it's worth it.

## Step 4 — The base building blocks (in `Erp.Shared.BuildingBlocks`)

A couple of small base classes every module's aggregates share:

```csharp
namespace Erp.Shared.BuildingBlocks;

public abstract class Entity<TId> where TId : struct   // TId is the strongly-typed ID
{
    public TId Id { get; protected set; }
}

public abstract class AggregateRoot<TId> : Entity<TId> where TId : struct   // repeat the constraint the base requires
{
    private readonly List<IDomainEvent> _events = new();
    public IReadOnlyList<IDomainEvent> DomainEvents => _events;
    protected void Raise(IDomainEvent e) => _events.Add(e);   // record "something happened"
    public void ClearEvents() => _events.Clear();
}

public interface IDomainEvent { }   // a marker; the actual events are records

public sealed class DomainException(string message) : Exception(message);
```

> ⚠️ **Don't drop the `where TId : struct` on `AggregateRoot`.** `Entity<TId>` requires `TId` to be a value
> type (`struct`), and a derived class must *repeat* any constraint its base declares. Leave it off
> `AggregateRoot<TId>` and the compiler stops you cold with
> *`CS0453: The type 'TId' must be a non-nullable value type`* — an easy one to hit, since it's tempting to
> write just `: Entity<TId>`.

> ❓ **"Why put these in a shared project instead of the Sales module?"** Because they're not about Sales —
> they're the generic scaffolding *any* aggregate uses. When the Inventory module arrives in Phase 5, its
> `StockItem` aggregate reuses the exact same `AggregateRoot` and `IDomainEvent`. Shared base types, per-
> module business objects.

## Step 5 — The rich `SalesOrder` (this is the payoff)

Now the good version. Notice the design choices:

- **No public setters.** State only changes through methods that enforce rules.
- **`Total` is *calculated*** from the lines every time — it can't drift, because it's never stored
  separately.
- **`Status` is a real enum with allowed transitions**, not a free-for-all string.
- **Lines are added through the root**, keeping the aggregate consistent.
- **Business methods record domain events.**

```csharp
namespace Erp.Modules.Sales.Domain;

public sealed class SalesOrder : AggregateRoot<SalesOrderId>
{
    private readonly List<SalesOrderLine> _lines = new();
    public IReadOnlyList<SalesOrderLine> Lines => _lines;
    public CustomerId CustomerId { get; private set; }
    public SalesOrderStatus Status { get; private set; }

    // Total is COMPUTED from the lines — it literally cannot be wrong.
    public Money Total => _lines.Aggregate(Money.Zero, (sum, l) => sum + l.LineTotal);

    private SalesOrder() { }  // private — only the repository uses this to rebuild from the DB (Phase 4)

    // The only way to make a new SalesOrder — and it starts as Draft.
    public static SalesOrder Create(CustomerId customerId)
    {
        var order = new SalesOrder { CustomerId = customerId, Status = SalesOrderStatus.Draft };
        order.Raise(new SalesOrderCreated(customerId));   // no id yet — see the ⚠️ note below
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

    // The approval threshold is a BUSINESS RULE, so it lives here — not in a handler.
    public bool RequiresApproval => Total > ApprovalPolicy.Threshold;
}
```

### The three small pieces `SalesOrder` leans on

The class above mentions a **line**, an **approval threshold**, and three **events** we haven't written yet.
They're tiny, so here they are in full (all in `Sales/Domain/`, alongside `SalesOrder`):

```csharp
namespace Erp.Modules.Sales.Domain;

// One line of the order. Its total is COMPUTED (price × quantity) — never stored, so it can't drift.
// (Money and Quantity are the value objects from Step 7; the * operator is defined there.)
public sealed class SalesOrderLine
{
    public ProductId ProductId { get; }
    public Quantity  Quantity  { get; }
    public Money     UnitPrice { get; }
    public Money LineTotal => UnitPrice * Quantity.Value;   // used by SalesOrder.Total

    public SalesOrderLine(ProductId productId, Quantity quantity, Money unitPrice)
    {
        ProductId = productId;
        Quantity  = quantity;
        UnitPrice = unitPrice;
    }
}

// The approval threshold is a business constant, so it gets ONE home. RequiresApproval reads it.
public static class ApprovalPolicy
{
    public static readonly Money Threshold = new(50_000m);
}
```

The three domain events are just records that state *what happened* — each implements the `IDomainEvent`
marker from `Erp.Shared.BuildingBlocks` (Step 4):

```csharp
namespace Erp.Modules.Sales.Domain;

public sealed record SalesOrderCreated(CustomerId CustomerId) : IDomainEvent;                    // no id yet (see ⚠️ below)
public sealed record SalesOrderSubmitted(SalesOrderId Id, CustomerId CustomerId, Money Total) : IDomainEvent;
public sealed record SalesOrderCancelled(SalesOrderId Id) : IDomainEvent;
```

### Let's walk through the `Submit` method line by line

This one method is the whole philosophy of the phase in miniature:

```csharp
public void Submit(bool approvalGranted)
{
    if (Status != SalesOrderStatus.Draft)                    // (a)
        throw new DomainException($"Only draft orders can be submitted (was {Status}).");
    if (_lines.Count == 0)                                   // (b)
        throw new DomainException("Cannot submit an empty order.");
    if (RequiresApproval && !approvalGranted)                // (c)
        throw new DomainException("Order exceeds approval threshold and needs manager approval.");
    Status = SalesOrderStatus.Submitted;                     // (d)
    Raise(new SalesOrderSubmitted(Id, CustomerId, Total));   // (e)
}
```

- **(a) The guard clauses come first.** Before changing *anything*, `Submit` checks every rule that could
  make submitting illegal. "Only a draft can be submitted." If already Submitted or Cancelled, it throws
  and *stops*. This is the object *defending itself*.
- **(b) "You can't submit an empty order."** A business truth expressed as plain code — a non-programmer
  could read it and nod.
- **(c) The approval rule.** `RequiresApproval` is that `Total > ₱50,000` property. If approval is needed
  but wasn't granted, refuse. The order *itself* knows the threshold — the handler doesn't.
- **(d) Only *after* every check passes** does the status change. If any check threw, we never reach this
  line, so an order can never end up Submitted while breaking a rule. That guarantee is the entire value of
  putting the rule here.
- **(e) Record that it happened.** `Raise(new SalesOrderSubmitted(...))` drops a note. Other code — or the
  Inventory module — can react later (reserve stock, send a confirmation) without the `SalesOrder` doing
  those side effects itself. (Full story in Phase 5 & 7.)

The payoff: the handler in Phase 4/5 will just call `order.Submit(approved)` and won't re-check *any* of
this — because the `SalesOrder` already guarantees it.

> ⚠️ **A subtlety about the id inside `SalesOrderCreated`.** A brand-new order hasn't been saved yet, so the
> database hasn't assigned its identity — `order.Id` is still the default `SalesOrderId(0)` when `Create()`
> runs. That's exactly why the event above is `SalesOrderCreated(CustomerId)` with **no id**: the real id
> only becomes meaningful *after* the insert, so we don't pretend to have it yet. (The other clean option is
> to let the **repository stamp `Id`** on the aggregate right after the `INSERT` returns `SCOPE_IDENTITY()`
> and dispatch events *after* commit — which we do anyway in Phase 7 — so a handler could read the real id.
> We take the simpler route here.) The trap to avoid is *assuming* an id is usable before the order is
> persisted. Later events like `SalesOrderSubmitted` *do* carry the id, because by then the order was loaded
> from the database with its real id.

> ❓ **"Why throw an exception instead of returning false or an error code?"** Because submitting a
> non-draft order isn't a normal outcome to politely report — it's a *rule violation that should never
> happen* if the rest of the code is correct. Throwing makes it *loud and unignorable*: the operation stops
> dead, and the caller can't accidentally continue as if it worked. A thrown `DomainException` can't be
> silently ignored the way a `false` can.

> ❓ **"The handler passes in `approvalGranted` — isn't that the handler making the decision?"** Subtle but
> important: the handler supplies a *fact* ("a manager did approve this"), but the `SalesOrder` decides what
> to *do* with it ("given my total, do I even need approval, and was it granted?"). The handler reports the
> world; the domain judges it.

> ⚠️ **Honest limitation — approval here is *not* actually enforced, and you must know that.** The domain
> correctly decides *whether* approval is **needed** (`RequiresApproval`), but `Submit(bool approvalGranted)`
> just *trusts* the boolean it's handed. Nothing in this beginner build checks that a **manager** actually
> granted it — whoever calls the endpoint could simply pass `approvalGranted: true` and sail through. The
> domain answers "*is* approval required?" but no one answers "*who is allowed to grant it?*" — that second
> question is **authorization**, a platform concern this course deliberately leaves out to stay focused.
>
> The [business tutorial](../plan-business-tutorial/08-approval-cancellation.md) does it the real way: a
> **separate, manager-role-gated `POST /sales-orders/{id}/approve` endpoint** (a JWT role check the caller
> can't fake), so approval becomes a genuine step performed by an authorized person — not a self-asserted flag.
> The contrast is worth burning in:
>
> ```
>  BEGINNER (this course) — approval is a SELF-ASSERTED FACT (unguarded)
>  ────────────────────────────────────────────────────────────────────
>    caller ──► Submit(approvalGranted: true) ──► domain: "is approval NEEDED?" ──► ✅ submitted
>               ▲ anyone can set this true; nothing verifies a manager said so
>
>  BUSINESS TUTORIAL — approval is a ROLE-GATED ACTION (guarded)
>  ────────────────────────────────────────────────────────────────────
>    caller  ──► POST /submit  ───────────────────────► order becomes PendingApproval
>    MANAGER ──► POST /approve ──[RequireRole("SalesManager")]──► ✅ Approved
>               ▲ the JWT role check rejects non-managers with 403 before any handler runs
> ```
>
> For *this* phase, `approvalGranted` is a fine way to learn **where the "is approval needed?" rule lives.**
> Just don't mistake it for a security control — it isn't one until an authorization check stands in front of it.

> 🚫 **What deliberately stays OUT of the `SalesOrder`:** how it's saved (SQL), who called it (the web), and
> safety-under-simultaneous-access (Phase 5's database guard). The `SalesOrder` can *express* the rule
> "reserve only if available," but it can't, by itself, make that safe when two people order at once. Keep
> that line sharp — it's the theme of Phase 5.

## Step 6 — `SalesOrderStatus` as a state machine

An order's status can't jump around arbitrarily. Encode the legal moves:

```csharp
public enum SalesOrderStatus { Draft, Submitted, Cancelled, Completed }
```

Allowed transitions (a real business rule — write it down, don't leave it implied):

```
Draft     → Submitted, Cancelled
Submitted → Completed, Cancelled
Cancelled → (nowhere — it's final)
Completed → (nowhere — it's final)
```

We turn that table into a small `CanTransitionTo` lookup so illegal moves are *impossible*, not merely
frowned upon — this is exactly what `Cancel()` above calls. (An extension method must live in a `static`
class, so we give it one.)

```csharp
namespace Erp.Modules.Sales.Domain;

public static class SalesOrderStatusRules
{
    private static readonly Dictionary<SalesOrderStatus, SalesOrderStatus[]> _allowed = new()
    {
        [SalesOrderStatus.Draft]     = new[] { SalesOrderStatus.Submitted, SalesOrderStatus.Cancelled },
        [SalesOrderStatus.Submitted] = new[] { SalesOrderStatus.Completed, SalesOrderStatus.Cancelled },
        [SalesOrderStatus.Cancelled] = Array.Empty<SalesOrderStatus>(),   // final — nowhere to go
        [SalesOrderStatus.Completed] = Array.Empty<SalesOrderStatus>(),   // final — nowhere to go
    };

    // An extension method, so you can write the readable `Status.CanTransitionTo(Cancelled)`.
    public static bool CanTransitionTo(this SalesOrderStatus from, SalesOrderStatus to) =>
        _allowed[from].Contains(to);
}
```

*(Analogy: a board game where pieces physically can't move to illegal squares.)*
[Phase 7](08-phase-7-remaining-slices.md) puts this same table to work when it wires up cancel/complete for
real, and the business tutorial adds an explicit manager `Approved` state on top of this.

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

Reading `Money` closely, because it packs several ideas into a few lines:

- **`readonly record struct`** — `struct` = a small lightweight value; `readonly` = never changes after
  creation; `record` = handy equality (two `Money(10)`s are equal). Together: a tiny, unchangeable,
  value-compared wrapper around a `decimal`. Exactly what "a value object" means.
- **The constructor throws on negatives:** the *only* way a `Money` can exist is with a non-negative
  amount. There is no such thing as a negative `Money` in this program. Ever.
- **`operator +`** lets you write `priceA + priceB` and get a valid `Money` back.
- **`operator >`** lets `Total > ApprovalPolicy.Threshold` read like plain English in the `SalesOrder`.

You **cannot construct** a negative price or a zero quantity — the object refuses. Invalid states become
*unrepresentable*.

> ❓ **"Why bother? A `decimal` for money works fine."** It works until it doesn't. With a raw `decimal`,
> nothing stops `-50` from being a price, or you accidentally adding a price to a quantity (both are just
> numbers to the compiler). With `Money` and `Quantity` as distinct types: negatives are impossible, and
> `Money + Quantity` won't even compile. You've turned runtime bugs into "the code won't build" — always
> cheaper. Same idea as strongly-typed IDs, applied to values.

> ❓ **"What's the private `SalesOrder()` constructor for?"** For *your* code, you always use
> `SalesOrder.Create(...)`. The private constructor exists purely so the **repository** (Phase 4) can
> rebuild a `SalesOrder` from database rows. Loading from the DB isn't "creating a new order," so it uses a
> separate, private door your feature code can't see.

> 🚫 **Don't over-model.** `Money` here is single-currency on purpose — this pretend company uses one
> currency. Adding multi-currency now would build for a requirement that doesn't exist. If it ever becomes
> real, `Money` is the *one* place it changes.

## What to produce in this phase

- `Erp.Shared.BuildingBlocks`: `Entity<TId>`, `AggregateRoot<TId>`, `IDomainEvent`, `DomainException`.
- `Sales/Domain`: the IDs, `SalesOrder`, `SalesOrderLine`, `Money`, `Quantity`, `SalesOrderStatus` +
  transition rules, `ApprovalPolicy` (the threshold), and the domain events (`SalesOrderCreated`,
  `SalesOrderSubmitted`, `SalesOrderCancelled`).
- **Unit tests for the domain** (yes, now — they're basically free): submitting a non-draft throws; an
  empty order can't be submitted; over-threshold without approval throws; illegal status transitions throw;
  `Money`/`Quantity` reject bad values.

**NOT now:** no repositories, no database, no handlers, no web. The domain compiles and is tested with
*zero* infrastructure — that isolation is the whole point.

## Common mistakes to avoid

- **Adding public setters "so the database mapper can fill it in."** Use the private constructor + `Create`
  factory; let the repository rebuild via the private constructor in Phase 4.
- **Putting the approval threshold in a handler.** It's a business rule; it lives on `SalesOrder`.
- **Modeling things nothing requires yet** (currencies, tax engines). Stop at the rules the actual
  operations need.
- **Putting `SalesOrder` in a shared project.** It belongs to the Sales module. Only the *generic* base
  types are shared.

---

## ✅ Check Yourself

1. **Give a concrete bug the dumb (anemic) `SalesOrder` allows that the smart (rich) one prevents.**
2. **Why are IDs strongly-typed structs instead of `long`, and what does that cost?**
3. **Why is `Total` a calculated property instead of a stored field you can set?**
4. **Which order rule lives on the entity, and why not in the handler?**
5. **Why do the base types (`AggregateRoot`, `IDomainEvent`) live in `Shared.BuildingBlocks` but
   `SalesOrder` lives in the Sales module?**

---

## 👨‍🏫 Answer Key

**1. Concrete bug the anemic `SalesOrder` allows that the rich one prevents.**
With the anemic version, any code can write `order.Status = "Banana"` or add a line and forget to update
`Total`, so the total no longer matches the lines. The rich version prevents both: `Status` has a `private
set` and only changes through methods like `Submit()`/`Cancel()` that check the rules, and `Total` is
*computed* from the lines every time so it *can't* be out of sync.

**2. Why strongly-typed ID structs, and what does it cost?**
The compiler **stops you mixing them up** — you can't pass a `CustomerId` where a `SalesOrderId` is
expected; it won't compile. A plain `long` would silently accept the wrong id. The **cost**: each ID type
needs a small (~10-line) Dapper "type handler" (added in Phase 4). Cheap insurance.

**3. Why is `Total` calculated, not a stored settable field?**
A stored, settable total can *drift*: change the lines, forget to recompute, now it lies. Computing `Total`
from the lines on every read makes it **always correct by construction** — no separate copy to desync, no
setter to misuse.

**4. Which rule lives on the entity, and why not the handler?**
The **approval threshold** (`RequiresApproval => Total > ApprovalPolicy.Threshold`, checked inside
`Submit()`). It lives on `SalesOrder` because it's a rule about the order's *own state*. As an `if` in a
handler it would be easy to forget in another handler and would make the domain no longer the single source
of truth. Put the rule where the data is.

**5. Why are base types shared but `SalesOrder` module-owned?**
`AggregateRoot`/`IDomainEvent` are generic scaffolding *any* aggregate in *any* module reuses — so they
live in `Shared.BuildingBlocks`. `SalesOrder` is a specific business object owned by the Sales department —
so it lives in `Sales/Domain/`. Sharing it would let other modules depend on Sales' internals, breaking the
island rule.

> ✅ **You're ready for Phase 4 if:** you can explain "anemic vs. rich" with a concrete bug, and you get
> *why* `Total` is computed and `Status` is guarded. This phase is the conceptual heart of the project.
> Bonus: your Phase 3 unit tests passing is proof you understood it.
