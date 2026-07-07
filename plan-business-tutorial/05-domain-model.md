# Phase 5 — The Domain Model: A `SalesOrder` That Knows Its Own Rules

> 📎 Slow version of *why* rich domain objects beat dumb data bags:
> [beginner Phase 3](../plan-beginner-friendly/04-phase-3-domain.md). Here we apply that exact pattern to
> a realistic sales order with many rules.

---

> ⏱️ **Time for this phase:** ~1.5–2 hr. This is the conceptual heart — the aggregate, value objects, and
> state machine. Pure C# with no database, so it's fast to type but worth reading slowly.

## 1. What we're building

The business objects at the center of everything: the `SalesOrder` aggregate (with its lines), the
supporting `Customer`, `Item`, `ItemCategory`, and `Inventory` domain types, the strongly-typed IDs, the
value objects (`Money`, `Quantity`), the `SalesOrderStatus` state machine, and the domain events. **No
database, no web** — pure C# that compiles and is unit-testable on its own.

## 2. Why it's designed this way

Every business rule that can be checked *in memory* lives here, on the object it's a rule about. That's
the payoff from [beginner Phase 3](../plan-beginner-friendly/04-phase-3-domain.md): handlers later just
*ask* the domain instead of policing it. The rules we'll encode here:

- Only a **Draft** order can have lines added/changed.
- An order's **total** is always the sum of its lines (computed, never drifts).
- **Status transitions** follow a strict state machine (Draft → PendingApproval → Approved, etc.).
- Whether an order is **high-value** (over a threshold, `RequiresApproval`) is the order's own knowledge —
  surfaced as a flag (see the ⚠️ note in §8 for exactly what it does in this tutorial).
- **Category quantity limits** and **role restrictions** are checked as lines go on.

Two rules that *cannot* be safely done in memory — **credit limit** (needs the customer's live balance)
and **inventory reservation** (needs atomic DB guarding) — are *expressed* here but *enforced* at the
handler/SQL level. We'll be explicit about that boundary; it's the "policy vs. atomicity" split from
[beginner Phase 5](../plan-beginner-friendly/06-phase-5-inventory-concurrency.md).

## 3. Strongly-typed IDs and value objects

Base types live in `Erp.Shared.BuildingBlocks`; the `SalesOrder` aggregate and its IDs/value objects live
in `Modules/Sales/Domain` (this module owns them — no other module references them):

```csharp
public readonly record struct SalesOrderId(long Value);
public readonly record struct CustomerId(long Value);
public readonly record struct ItemId(long Value);
public readonly record struct UserId(long Value);
```

> 📎 Why distinct ID types instead of raw `long` (compiler stops you mixing a `CustomerId` and a
> `SalesOrderId`) — [beginner Phase 3 §3](../plan-beginner-friendly/04-phase-3-domain.md). Cost: a small
> Dapper `TypeHandler` per ID, added in the repository.

Value objects that make invalid states impossible:

```csharp
public readonly record struct Quantity
{
    public int Value { get; }
    public Quantity(int value) =>
        Value = value > 0 ? value : throw new DomainException("Quantity must be greater than zero.");
}

public readonly record struct Money
{
    public decimal Amount { get; }
    public static readonly Money Zero = new(0m);
    public Money(decimal amount) =>
        Amount = amount >= 0 ? amount : throw new DomainException("Money cannot be negative.");

    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount);
    public static Money operator *(Money m, int qty) => new(m.Amount * qty);
    public static bool operator >(Money a, Money b) => a.Amount > b.Amount;
    public static bool operator <(Money a, Money b) => a.Amount < b.Amount;
    public static bool operator >=(Money a, Money b) => a.Amount >= b.Amount;
    public static bool operator <=(Money a, Money b) => a.Amount <= b.Amount;
}
```

## 4. The status state machine

```csharp
public enum SalesOrderStatus
{
    Draft = 0,            // being edited
    PendingApproval = 1,  // submitted, inventory reserved, awaiting a manager
    Approved = 2,         // manager approved; will be fulfilled
    Cancelled = 3,        // cancelled; reservations released
    Rejected = 4          // manager rejected; reservations released
}
```

Legal transitions — a real business rule, encoded so illegal moves are *impossible*:

```
Draft            → PendingApproval, Cancelled
PendingApproval  → Approved, Rejected, Cancelled
Approved         → Cancelled
Cancelled        → (final)
Rejected         → (final)
```

Drawn as a map (follow the arrows; there's no way back out of the two shaded end states):

```
                 submit                approve
   ┌─────────┐  ────────► ┌──────────────────┐  ────────► ┌──────────┐
   │  Draft  │            │  PendingApproval │            │ Approved │
   └────┬────┘            └───┬──────────┬───┘            └────┬─────┘
        │                     │  reject  │                     │
        │ cancel              │          │ cancel              │ cancel
        │                     ▼          │                     │
        │              ┌────────────┐    │                     │
        │              │  Rejected  │◄───┘                     │
        │              │  (final)   │                          │
        │              └────────────┘                          │
        ▼                                                      ▼
   ┌───────────────────────────  Cancelled (final)  ◄──────────┘
   └──────────────────────────────────────────────┘
```

*Reserved stock is released on the way into `Cancelled`/`Rejected`; the balance charged at `Approved` is
reversed if an approved order is later cancelled. The diagram is the "what's legal"; the handlers
([Phases 7–8](07-submit-reserve-concurrency.md)) attach the side effects to each arrow.*

```csharp
public static class SalesOrderStatusRules
{
    private static readonly Dictionary<SalesOrderStatus, SalesOrderStatus[]> _allowed = new()
    {
        [SalesOrderStatus.Draft]           = new[] { SalesOrderStatus.PendingApproval, SalesOrderStatus.Cancelled },
        [SalesOrderStatus.PendingApproval] = new[] { SalesOrderStatus.Approved, SalesOrderStatus.Rejected, SalesOrderStatus.Cancelled },
        [SalesOrderStatus.Approved]        = new[] { SalesOrderStatus.Cancelled },
        [SalesOrderStatus.Cancelled]       = Array.Empty<SalesOrderStatus>(),
        [SalesOrderStatus.Rejected]        = Array.Empty<SalesOrderStatus>(),
    };

    public static bool CanTransitionTo(this SalesOrderStatus from, SalesOrderStatus to) =>
        _allowed[from].Contains(to);
}
```

> 📎 Why a lookup table beats scattered `if`s — [beginner Phase 7](../plan-beginner-friendly/08-phase-7-remaining-slices.md).
> All the transition rules sit in one readable grid.

## 5. The `ItemCategory` rules (restrictions + limits)

A small domain type carrying the category rules from the schema:

```csharp
public sealed class ItemCategory
{
    public long Id { get; }
    public string Name { get; }
    public int MaxQuantityPerOrder { get; }
    public string? RequiredRole { get; }   // null = anyone

    public ItemCategory(long id, string name, int maxQuantityPerOrder, string? requiredRole)
    { Id = id; Name = name; MaxQuantityPerOrder = maxQuantityPerOrder; RequiredRole = requiredRole; }

    public void EnsureRoleAllowed(string callerRole)
    {
        if (RequiredRole is not null && !string.Equals(callerRole, RequiredRole, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(callerRole, "Admin", StringComparison.OrdinalIgnoreCase))
            throw new DomainException(
                $"Category '{Name}' requires role '{RequiredRole}'.");
    }

    public void EnsureQuantityAllowed(int quantityOnOrder)
    {
        if (quantityOnOrder > MaxQuantityPerOrder)
            throw new DomainException(
                $"Category '{Name}' allows at most {MaxQuantityPerOrder} units per order (requested {quantityOnOrder}).");
    }
}
```

Notice these are **rules expressed as methods on the thing they're about** — a category knows its own
restrictions. The handler will hand the caller's role to `EnsureRoleAllowed`; the *fact* (the role)
comes from outside, the *judgement* lives here. Same shape as `SalesOrder.Submit(approvalGranted)` from
the beginner plan.

## 6. The `SalesOrder` aggregate

This is the centerpiece. Read the comments — every design choice is deliberate.

```csharp
public sealed class SalesOrder : AggregateRoot<SalesOrderId>
{
    private readonly List<SalesOrderLine> _lines = new();
    public IReadOnlyList<SalesOrderLine> Lines => _lines;

    public string OrderNumber { get; private set; } = default!;
    public CustomerId CustomerId { get; private set; }
    public SalesOrderStatus Status { get; private set; }
    public UserId CreatedBy { get; private set; }
    public UserId? ApprovedBy { get; private set; }   // set when a manager approves (used by the repo on update)
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();  // set by repo on load

    // Total is COMPUTED — it can never disagree with the lines.
    public Money Total => _lines.Aggregate(Money.Zero, (sum, l) => sum + l.LineTotal);

    // The approval threshold is the ORDER'S own knowledge (a domain rule).
    public bool RequiresApproval => Total > ApprovalPolicy.Threshold;

    private SalesOrder() { }   // for the repository to rehydrate from the DB

    public static SalesOrder Create(string orderNumber, CustomerId customerId, UserId createdBy)
    {
        var order = new SalesOrder
        {
            OrderNumber = orderNumber,
            CustomerId = customerId,
            CreatedBy = createdBy,
            Status = SalesOrderStatus.Draft
        };
        order.Raise(new SalesOrderCreated(order.Id, customerId, createdBy));
        return order;
    }

    public void AddLine(ItemId itemId, Quantity qty, Money unitPrice)
    {
        if (Status != SalesOrderStatus.Draft)
            throw new DomainException("Lines can only be changed on a draft order.");
        // Merge into an existing line for the same item, or add a new one.
        var existing = _lines.FirstOrDefault(l => l.ItemId == itemId);
        if (existing is not null) existing.IncreaseQuantity(qty);
        else _lines.Add(new SalesOrderLine(itemId, qty, unitPrice));
    }

    public void ClearLines()
    {
        if (Status != SalesOrderStatus.Draft)
            throw new DomainException("Lines can only be changed on a draft order.");
        _lines.Clear();
    }

    public void Submit()
    {
        // Guards FIRST — never mutate state until every check has passed.
        if (_lines.Count == 0)
            throw new DomainException("Cannot submit an order with no lines.");
        // Transition throws (before changing Status) if the move is illegal, e.g. not a Draft.
        Transition(SalesOrderStatus.PendingApproval);
        Raise(new SalesOrderSubmitted(Id, CustomerId, Total));
    }

    public void Approve(UserId approver)
    {
        Transition(SalesOrderStatus.Approved);
        ApprovedBy = approver;
        Raise(new SalesOrderApproved(Id, CustomerId, Total, approver));
    }

    public void Reject()
    {
        Transition(SalesOrderStatus.Rejected);
        Raise(new SalesOrderRejected(Id));
    }

    public void Cancel()
    {
        Transition(SalesOrderStatus.Cancelled);
        Raise(new SalesOrderCancelled(Id));
    }

    // Single guarded gate for EVERY status change.
    private void Transition(SalesOrderStatus to)
    {
        if (!Status.CanTransitionTo(to))
            throw new DomainException($"Cannot move a {Status} order to {to}.");
        Status = to;
    }
}
```

> ⚠️ **A subtlety in `Create`: the id inside `SalesOrderCreated` is provisional.** The order isn't saved yet
> when `Create()` runs, so the database hasn't handed out its identity — `order.Id` is still `SalesOrderId(0)`
> at the moment we raise `SalesOrderCreated(order.Id, …)`. Don't rely on that id being real. Two clean fixes,
> both fine here: **(a)** leave the id *out* of the created event (`SalesOrderCreated(CustomerId, CreatedBy)`
> is enough — the id only matters once persisted); or **(b)** have the repository stamp `Id` on the aggregate
> right after `INSERT … SELECT SCOPE_IDENTITY()`, and dispatch events **after commit** (which we already do —
> [Phase 8 §6](08-approval-cancellation.md)), so any handler reads the persisted id. The trap is *assuming*
> `SalesOrderCreated.Id` is usable before the order is saved.

### Line by line: the `Submit` method

```csharp
public void Submit()
{
    if (_lines.Count == 0)                          // (a) an empty order makes no sense to submit
        throw new DomainException("Cannot submit an order with no lines.");
    Transition(SalesOrderStatus.PendingApproval);  // (b) legal move? Draft→PendingApproval only — mutates last.
    Raise(new SalesOrderSubmitted(Id, CustomerId, Total));  // (c) announce it happened
}
```

- **(a)** Check the non-state rule *before* touching any state — you can't submit nothing. Doing this
  first means a failed submit leaves the order exactly as it was (still `Draft`), never half-mutated.
- **(b)** Only now the guarded transition: is this move even legal? If the order is already Approved or
  Cancelled, `Transition` throws *before* assigning `Status`, and nothing changes. The order defends
  itself. **Guards before mutation** is the discipline from
  [beginner Phase 3](../plan-beginner-friendly/04-phase-3-domain.md) — validate everything, *then* change
  state, so a throw can never leave the object in a broken in-between state.
- **(c)** Record the fact. The *inventory reservation* is **not** done here — that's an
  atomicity-under-concurrency concern the handler + SQL own (Phase 7). The domain *expresses* "this got
  submitted"; it does not reach into a database. Keep that boundary sharp.

> ❓ **You might be wondering: "Why doesn't `Submit()` reserve the inventory right here? It's part of
> submitting."** Because reserving stock safely requires an *atomic database operation* — and the domain
> is forbidden from touching the database (it must stay pure and unit-testable). The domain says "a
> submit happened"; the **handler** orchestrates the actual reservation via SQL inside a transaction.
> This is exactly the "policy in the domain, atomicity in SQL" division from
> [beginner Phase 5](../plan-beginner-friendly/06-phase-5-inventory-concurrency.md). If you put a DB call
> in `Submit()`, you couldn't unit-test the state machine without a database — and you'd have broken the
> dependency rule.

### The `SalesOrderLine`

```csharp
public sealed class SalesOrderLine
{
    public ItemId ItemId { get; }
    public Quantity Quantity { get; private set; }
    public Money UnitPrice { get; }
    public Money LineTotal => UnitPrice * Quantity.Value;

    public SalesOrderLine(ItemId itemId, Quantity qty, Money unitPrice)
    { ItemId = itemId; Quantity = qty; UnitPrice = unitPrice; }

    public void IncreaseQuantity(Quantity extra) => Quantity = new Quantity(Quantity.Value + extra.Value);
}
```

## 7. The `Customer` domain type (blocking + credit)

```csharp
public sealed class Customer
{
    public CustomerId Id { get; }
    public string Name { get; }
    public bool IsBlocked { get; }
    public Money CreditLimit { get; }
    public Money OutstandingBalance { get; }

    public Customer(CustomerId id, string name, bool isBlocked, Money creditLimit, Money outstanding)
    { Id = id; Name = name; IsBlocked = isBlocked; CreditLimit = creditLimit; OutstandingBalance = outstanding; }

    public void EnsureNotBlocked()
    {
        if (IsBlocked)
            throw new DomainException($"Customer '{Name}' is blocked and cannot place orders.");
    }

    public void EnsureWithinCreditLimit(Money orderTotal)
    {
        if (OutstandingBalance + orderTotal > CreditLimit)
            throw new DomainException(
                $"Order total {orderTotal.Amount:C} would exceed the credit limit. " +
                $"Available credit: {(CreditLimit.Amount - OutstandingBalance.Amount):C}.");
    }
}
```

> ❓ **You might be wondering: "Credit limit needs the customer's live balance from the DB — isn't that
> a database concern, so shouldn't it be in SQL?"** Subtle and important. The *decision* ("total +
> balance > limit → reject") is pure arithmetic and business policy → it lives here in the domain, fully
> unit-testable by constructing a `Customer` with numbers. The *loading* of those numbers is the
> handler's job (it calls the repository, gets a `Customer`, then asks it). Unlike inventory, credit
> isn't a race that oversells the last unit at microsecond scale — a slightly-stale balance is a normal
> business risk, not a correctness bug. So credit = domain rule; inventory reservation = SQL atomic
> guard. Different tools for different risks.

> ⚠️ **Important limitation — read this so you're not surprised later.** `OutstandingBalance` only counts
> what the customer *already owes on approved orders*. In this tutorial we bump it at **approval**
> ([Phase 8](08-approval-cancellation.md)) — **not** when an order is merely created or submitted. That
> keeps the flow simple, but it has a real consequence: **draft and pending orders don't yet count against
> the credit limit.** So a customer with $500 of credit left could, in theory, create *ten* separate
> orders that each individually fit under $500 — because none of them has been approved yet, so none has
> touched the balance. Each order passes its own check; together they'd blow the limit.
>
> Is that a bug? For a *learning* build, it's an acceptable simplification (and a real company might even
> accept the risk). For a *real* ERP it usually isn't — you'd want the limit to consider "already owed **+**
> value of orders currently in flight." The clean way to do that is a **committed-plus-pending exposure**
> figure: either a query that sums the customer's non-cancelled orders, or a dedicated `ReservedCredit`
> column adjusted at submit and released on cancel/reject (a *matched pair*, exactly like inventory
> reserve/release). We flag it here, list it on the [Phase 13 checklist](13-wrap-up-checklist.md), and leave
> it as the first correctness upgrade you'd make — done properly it's a great exercise in the same "reserve
> now, release on the reverse transition" muscle you build for stock. **The point for now: know that
> `EnsureWithinCreditLimit` guards against a *single* order's total, not the customer's whole in-flight
> pipeline.**

## 8. Domain events and the approval policy

```csharp
// IDomainEvent already lives in Erp.Shared.BuildingBlocks (Phase 1), next to Entity/AggregateRoot —
// every module's events reuse it. We do NOT redeclare it here; we just implement it.
public sealed record SalesOrderCreated(SalesOrderId Id, CustomerId CustomerId, UserId CreatedBy) : IDomainEvent;
public sealed record SalesOrderSubmitted(SalesOrderId Id, CustomerId CustomerId, Money Total) : IDomainEvent;
public sealed record SalesOrderApproved(SalesOrderId Id, CustomerId CustomerId, Money Total, UserId Approver) : IDomainEvent;
public sealed record SalesOrderRejected(SalesOrderId Id) : IDomainEvent;
public sealed record SalesOrderCancelled(SalesOrderId Id) : IDomainEvent;

public static class ApprovalPolicy
{
    public static readonly Money Threshold = new(10_000m);  // orders over $10k are flagged high-value
}
```

> ⚠️ **What the approval threshold does — and doesn't — do in this tutorial (read this).** In *this*
> build, **every** submitted order routes through a manager: `Submit()` always moves `Draft →
> PendingApproval`, and a manager `Approve`s it ([Phase 8](08-approval-cancellation.md)). The
> `RequiresApproval` flag (`Total > Threshold`) is a **surfaced business fact**, not a workflow branch —
> it's there so a manager UI can highlight *high-value* orders for extra scrutiny, and it's unit-tested as
> a *property*. We deliberately did **not** wire it to auto-approve small orders, because that would spread
> the approval + balance logic across two slices and muddy a beginner's first pass. **Making small orders
> auto-approve on submit is the [Phase 13](13-wrap-up-checklist.md) extension exercise** — a great next
> step once the uniform flow is clear. (If your company genuinely wants "all orders need sign-off," the
> uniform flow is also a perfectly valid real policy.)

> 📎 **Two courses model approval differently — on purpose.** The
> [beginner plan](../plan-beginner-friendly/04-phase-3-domain.md) folds approval into `Submit(bool
> approvalGranted)` — the caller *asserts* approval as it submits, to keep that course to a single write
> path. This tutorial splits it into a **dedicated manager `Approve` step** with its own role-gated
> endpoint, which is the more realistic ERP shape. Same threshold idea, different workflow granularity;
> neither contradicts the other.

## 9. Why each class exists

| Class | Why it exists |
|---|---|
| `SalesOrder` | The aggregate root — the single guardian of order invariants; the only way status/lines change. |
| `SalesOrderLine` | Part of the aggregate; owns its quantity/price/line-total. Only reachable through the order. |
| `SalesOrderStatus` + rules | Encodes the legal lifecycle as data, so illegal transitions are impossible. |
| `Money`, `Quantity` | Make negative money / non-positive quantities *unrepresentable*. |
| `Customer` | Owns the blocking and credit-limit *decisions* (handler supplies the live numbers). |
| `ItemCategory` | Owns the role-restriction and per-order quantity-limit *decisions*. |
| Strongly-typed IDs | Compiler-enforced protection against mixing up ids. |
| Domain events | Record business facts for after-commit reactions (Phase 8), without wiring side effects into the domain. |
| `ApprovalPolicy` | One place for the approval threshold constant. |

## 10. Unit tests (write these now — they're free proof)

Because the domain touches nothing, testing is just objects (see [Phase 12](12-testing.md) for the full
suite). A taste:

```csharp
[Fact]
public void Cannot_add_lines_to_a_submitted_order()
{
    var order = SalesOrder.Create("SO-1", new CustomerId(1), new UserId(1));
    order.AddLine(new ItemId(1), new Quantity(1), new Money(25));
    order.Submit();
    Assert.Throws<DomainException>(() =>
        order.AddLine(new ItemId(2), new Quantity(1), new Money(40)));
}

[Fact]
public void Order_over_threshold_is_flagged_as_high_value()   // tests the FLAG, not a workflow branch
{
    var order = SalesOrder.Create("SO-2", new CustomerId(1), new UserId(1));
    order.AddLine(new ItemId(1), new Quantity(1), new Money(15_000));
    Assert.True(order.RequiresApproval);   // surfaced so a manager UI can prioritise; see the ⚠️ note above
}

[Fact]
public void Blocked_customer_is_rejected()
{
    var c = new Customer(new CustomerId(3), "Gamma", isBlocked: true, new Money(20_000), Money.Zero);
    Assert.Throws<DomainException>(() => c.EnsureNotBlocked());
}
```

## 11. Common mistakes & best practices

- ❌ **Public setters "for the mapper."** ✅ Private setters + `Create` factory + a private constructor
  the repository uses to rehydrate. Public setters reopen the dumb-data-bag hole.
- ❌ **Storing `Total` as a settable field.** ✅ Compute it from the lines; it can't drift. (We *also*
  persist a snapshot in the DB for fast search — the domain's computed value is the authority; the DB
  column is a display convenience, exactly the distinction from
  [beginner Phase 6](../plan-beginner-friendly/07-phase-6-queries.md).)
- ❌ **Calling the database from inside a domain method** (e.g. reserving stock in `Submit`). ✅ Domain
  stays pure; handlers orchestrate DB work.
- ❌ **Modeling things no rule needs** (multi-currency, tax engines). ✅ Stop at the rules the tutorial
  actually enforces.
- ✅ **Best practice: one `Transition` gate.** Every status change goes through it, so the state machine
  can never be bypassed.

---

## ✅ Check Yourself

1. Why is credit-limit a *domain* rule but inventory reservation a *SQL* one, even though both "need
   database data"?
2. Why doesn't `SalesOrder.Submit()` reserve inventory itself?
3. How does the design guarantee `Total` can never disagree with the lines?
4. Where does the *fact* of the caller's role come from, and where is it *judged*, for a category
   restriction?

> 👉 Next: [Phase 6 — The Create Sales Order slice, end to end](06-create-order.md).
