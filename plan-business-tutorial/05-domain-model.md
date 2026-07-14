# Phase 5 — The Domain Model: A `SalesOrder` That Knows Its Own Rules

> Self-contained: every rule is explained here. Money is in **Philippine Pesos (₱)**.

---

> ⏱️ **Time for this phase:** ~1.5–2 hr. This is the conceptual heart — the aggregate, value objects, and
> state machine. Pure C# with no database, so it's fast to type but worth reading slowly.

## 1. What we're building

The business objects at the center of everything: the `SalesOrder` aggregate (with its lines), the supporting
`Customer` and `ItemCategory` types, the strongly-typed IDs, the value objects (`Money`, `Quantity`), the
`SalesOrderStatus` state machine, and the domain events. **No database, no web** — pure C# that compiles and
is unit-testable on its own.

(Stock levels belong to the *Inventory* module, so no `Inventory` type appears here — [Phase 7](07-submit-reserve-concurrency.md)
introduces it behind a contract. And an `Item` needs only its price and category at order time, so it arrives
in [Phase 6](06-create-order.md) as a small read shape rather than an entity.)

## 2. Why it's designed this way

Every business rule that can be checked *in memory* lives here, on the object it's a rule about. That is the
whole payoff of a rich domain: handlers later just *ask* the domain instead of policing it themselves, so a
rule can't be enforced in one place and forgotten in another. The rules we'll encode here:

- Only a **Draft** order can have lines added/changed.
- An order's **total** is always the sum of its lines (computed, never drifts).
- **Status transitions** follow a strict state machine (Draft → PendingApproval → Approved, etc.).
- Whether an order is **high-value** (over a threshold, `RequiresApproval`) is the order's own knowledge —
  surfaced as a flag (see the ⚠️ note in §8 for exactly what it does in this tutorial).
- **Category quantity limits** and **role restrictions** are checked as lines go on.

Two rules that *cannot* be safely done in memory — **credit limit** (needs the customer's live balance)
and **inventory reservation** (needs atomic DB guarding) — are *expressed* here but *enforced* at the
handler/SQL level. We'll be explicit about that boundary every time we cross it. It's the **"policy vs.
atomicity" split**: deciding *whether something is allowed* is policy and belongs in the domain, while
making sure *two people doing it at once can't both win* is atomicity and belongs in SQL. [Phase 7](07-submit-reserve-concurrency.md)
is where that distinction stops being abstract.

## 3. Where every file in this phase goes

Everything in this phase is **pure C# in the Sales module's `Domain/` folder** (plus the shared base types
you already have). Nothing here references a database or the web. Here's the map — each code block below
names its file in a top comment, so you always know where it belongs:

```
src/Modules/Sales/Domain/
├── SalesOrders/                    # the SalesOrder aggregate — one file per type
│   ├── SalesOrder.cs               #   the aggregate root (§6)
│   ├── SalesOrderLine.cs           #   its child entity (§6)
│   ├── SalesOrderStatus.cs         #   the status enum (§4)
│   ├── SalesOrderStatusRules.cs    #   the legal-transition table (§4)
│   ├── SalesOrderEvents.cs         #   the five domain events (§8)
│   └── ApprovalPolicy.cs           #   the high-value threshold (§8)
├── Customers/
│   └── Customer.cs                 # §7
├── ItemCategories/
│   └── ItemCategory.cs             # §5
└── Common/                         # cross-cutting primitives, shared by every aggregate
    ├── Ids.cs                      #   the four strongly-typed IDs (§3a)
    └── ValueObjects.cs             #   Money, Quantity (§3a)
```

> 🔑 **Why a folder per aggregate?** Because the *folder* becomes the aggregate boundary — the exact thing
> this whole course is about drawing cleanly. Everything that belongs to the `SalesOrder` aggregate — its
> child line, the state machine it moves through, the events it raises, the one threshold it consults —
> lives together under `SalesOrders/`, so "what is part of this aggregate?" is answered by *what's in the
> folder*. Yet each type still gets its own file, so tools like "go to file" work and the one-type-per-file
> `SA1402` analyzer stays happy. `Customer` and `ItemCategory` are their own entities, so they get their own
> folders. The IDs and value objects belong to no single aggregate — they're shared vocabulary — so they sit
> in `Common/`. (This is the "folder per aggregate" layout compared in the box below; we adopt it as the
> course's default because it scales as the module grows and keeps the aggregate boundary visible in the
> file tree.)

The base types the domain builds on — `AggregateRoot<TId>`, `Entity<TId>`, `IDomainEvent`, `DomainException` —
already live in `Erp.Shared.BuildingBlocks` (you wrote them in [Phase 1 §4b](01-solution-setup.md)). If the
`where TId : struct` constraint or the `Raise`/`DomainEvents` pair isn't fresh, reread that section now —
this phase uses all of it. The domain uses those and nothing else.

> ⚠️ **Folders are organisation, not namespaces.** Every file below — no matter which folder it's in — stays
> in the flat `namespace Erp.Modules.Sales.Domain`. The `SalesOrders/` folder does *not* become a
> `…Domain.SalesOrders` namespace. That's a deliberate choice: the physical layout can change (and just did,
> if you followed the course's earlier drafts) without a single `using` moving in the handlers or repositories
> that consume these types. Folders are for humans reading the tree; the namespace is the compile-time
> contract, and it stays put.

### Other layouts you'll meet in production

Folder-per-aggregate is what we landed on, but it's a judgment call, not a law. Walk into three shops and
you'll find three answers to "where do domain types go," each with a real reason behind it. Here are the ones
you'll actually run into:

| Layout | What it is | Pros | Cons |
|---|---|---|---|
| **One type per file** | `SalesOrder.cs`, `SalesOrderLine.cs`, `SalesOrderStatus.cs`, … flat in `Domain/`, one class each. The C# community default; StyleCop's `SA1402` enforces it. | Any type is one filename away (`Ctrl-T`, "go to file"). Tiny diffs, almost no merge conflicts. Matches every tool's expectations out of the box. | Nothing groups an aggregate's parts — its story is smeared across a dozen files, and `Domain/` becomes one long wall of names with no visible boundaries. |
| **Cohesion grouping** | The aggregate and everything that serves it in *one file*; independent entities get their own. | The whole `SalesOrder` reads top-to-bottom in one place. Fewest files to hold in your head. | `SalesOrder.cs` holds types not named `SalesOrder` — which trips `SA1402` and surprises a newcomer hunting for a `SalesOrderStatus.cs` that isn't there. Files grow large over time. |
| **Folder per aggregate** *(this course)* | A `SalesOrders/` folder, one type per file inside; shared primitives in `Common/`. | Cohesion *and* one-type-per-file: the **folder** is the aggregate boundary, the files stay tool-friendly and `SA1402`-clean. Scales cleanly as an aggregate sprouts parts. | More tree to navigate; a folder holding a single small entity (`Customers/Customer.cs`) can feel ceremonious until the module grows. |
| **Nested child entities** | `SalesOrderLine` declared *inside* `SalesOrder` as a nested class, reached as `SalesOrder.Line`. | The compiler now enforces "a line only exists through its order" — you can't `new` one from outside. Ownership is unmissable. | Verbose call sites, awkward with Dapper/serialisation, and rare enough that most C# devs do a double-take. |

Here's the same domain — the exact types you're about to write — arranged each way, so the trade-offs stop
being abstract.

**One type per file** — strict `SA1402`. Even each event is its own file; the folder is a wall of names:

```
Domain/
├── SalesOrderId.cs
├── CustomerId.cs
├── ItemId.cs
├── UserId.cs
├── Money.cs
├── Quantity.cs
├── SalesOrderStatus.cs
├── SalesOrderStatusRules.cs
├── ApprovalPolicy.cs
├── SalesOrder.cs
├── SalesOrderLine.cs
├── SalesOrderCreated.cs
├── SalesOrderSubmitted.cs
├── SalesOrderApproved.cs
├── SalesOrderRejected.cs
├── SalesOrderCancelled.cs
├── Customer.cs
└── ItemCategory.cs          # 18 files for one module's domain
```

**Cohesion grouping** — five files; the aggregate and all its satellites travel together in `SalesOrder.cs`:

```
Domain/
├── Ids.cs             # SalesOrderId, CustomerId, ItemId, UserId
├── ValueObjects.cs    # Money, Quantity
├── SalesOrder.cs      # SalesOrder + SalesOrderLine + SalesOrderStatus
│                      #   + SalesOrderStatusRules + the 5 events + ApprovalPolicy
├── Customer.cs
└── ItemCategory.cs
```

**Folder per aggregate** — *what this course uses.* One type per file *inside* a per-aggregate folder. The
folder gives cohesion, the files stay `SA1402`-clean; shared primitives sit in their own `Common/`:

```
Domain/
├── SalesOrders/
│   ├── SalesOrder.cs
│   ├── SalesOrderLine.cs
│   ├── SalesOrderStatus.cs
│   ├── SalesOrderStatusRules.cs
│   ├── SalesOrderEvents.cs
│   └── ApprovalPolicy.cs
├── Customers/
│   └── Customer.cs
├── ItemCategories/
│   └── ItemCategory.cs
└── Common/
    ├── Ids.cs
    └── ValueObjects.cs      # Money, Quantity
```

**Nested child entities** — the *folder* looks like cohesion grouping, but the difference lives in the code:
`SalesOrderLine` moves *inside* `SalesOrder`, so it's only reachable as `SalesOrder.Line` and can't be `new`ed
from outside the aggregate:

```csharp
// SalesOrder.cs
public sealed class SalesOrder : AggregateRoot<SalesOrderId>
{
    private readonly List<Line> _lines = new();
    // ...factory, Submit(), Approve()... exactly as you'll write them below...

    public sealed class Line          // nested — the outside world says SalesOrder.Line
    {
        public ItemId ItemId { get; }
        public Quantity Quantity { get; private set; }
        public Money UnitPrice { get; }
        public Money LineTotal => UnitPrice * Quantity.Value;

        // internal so ONLY SalesOrder can build one — the compiler enforces "no orphan lines"
        internal Line(ItemId itemId, Quantity qty, Money unitPrice)
        { ItemId = itemId; Quantity = qty; UnitPrice = unitPrice; }
    }
}
```

Two more you'll hear argued about, which we deliberately skip. **`partial class` across files** — splitting
one aggregate over `SalesOrder.Behaviour.cs` and `SalesOrder.State.cs` — sounds like the best of both worlds,
but a class you have to reassemble from four files is usually *harder* to reason about; reach for it only when
a single type is genuinely enormous. And **domain-in-the-slice**, the strict-VSA purist's move of pushing each
entity into the feature that owns it: we reject it here for a concrete reason — `SalesOrder` is used by *six*
slices (create, submit, approve, cancel, update, get). A type shared by many slices belongs to the module, not
to any one of them; burying it in `CreateSalesOrder/` would force the other five to reach across a slice
boundary to find it. Same logic that keeps cross-module contracts up in `Shared`: a shared thing lives one
level above the things that share it.

> ⚠️ **The tooling has an opinion — and folder-per-aggregate mostly keeps it happy.** Turn on StyleCop or the
> equivalent `.editorconfig` analyzers and **`SA1402` fails the build on more than one type per file**. That's
> the rule that rules out cohesion grouping (a `SalesOrder.cs` full of other types), and it's a big reason we
> went with folder-per-aggregate instead — one type per file *is* its default. We do keep three pragmatic
> exceptions where the types are trivial and always change together: `Common/Ids.cs` (four one-line ID
> structs), `Common/ValueObjects.cs` (`Money` + `Quantity`), and `SalesOrders/SalesOrderEvents.cs` (the five
> event records). If your team runs `SA1402` strictly, split those too, or suppress the rule for just those
> files — either is fine, as long as it's a decision the team makes on purpose rather than three developers
> silently picking three and letting the folder drift into a mix nobody can predict.

> 🔑 **The honest takeaway:** none of these is *correct* with the rest *wrong*. What matters is that the layout
> maps to how the code actually changes, and that the whole team follows the same one. We picked
> folder-per-aggregate because the folder makes the aggregate boundary visible — which is the very thing this
> course teaches you to draw — and it scales without a rewrite as the module grows. A tiny throwaway module
> might reasonably stay flat with one-type-per-file; a huge one leans even harder on the folders. Pick
> deliberately, write it down, stay consistent.

## 3a. Strongly-typed IDs and value objects

Every file in the `Domain/` folder lives in the `Erp.Modules.Sales.Domain` namespace. Most also need
`using Erp.Shared.BuildingBlocks;` for `DomainException`, `IDomainEvent`, and `AggregateRoot<TId>` — the
bare ID structs are the exception, since they reference none of those. Here are the IDs, in their own file:

```csharp
// src/Modules/Sales/Domain/Common/Ids.cs (grouped — same one-line pattern; split them if you prefer)
namespace Erp.Modules.Sales.Domain;

public readonly record struct SalesOrderId(long Value);
public readonly record struct CustomerId(long Value);
public readonly record struct ItemId(long Value);
public readonly record struct UserId(long Value);
```

> 🔑 **Why distinct ID types instead of a raw `long`?** Because `long` lets you pass a `CustomerId` where a
> `SalesOrderId` was expected, and the compiler will happily agree — a bug you find in production, on the
> wrong row. Give each ID its own type and that mistake stops compiling. The cost is one line at the database
> boundary: Dapper doesn't know what a `CustomerId` is, so the repository unwraps it — `order.CustomerId.Value`
> going in, `new CustomerId(row.CustomerId)` coming back. You'll see exactly that in
> [Phase 6 §6](06-create-order.md). Keeping the unwrapping *in the repository* is the point: it's the only
> place that ever deals in bare `long`s.

Value objects that make invalid states impossible — both go in one `ValueObjects.cs`, which *does* need
the `BuildingBlocks` header (for `DomainException`):

```csharp
// src/Modules/Sales/Domain/Common/ValueObjects.cs
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

public readonly record struct Quantity
{
    public int Value { get; }
    public Quantity(int value) =>
        Value = value > 0 ? value : throw new DomainException("Quantity must be greater than zero.");
}

// ₱ — a plain non-negative amount.
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

The status enum and its rules are the first two files in the `SalesOrders/` folder — the aggregate's
lifecycle, sitting right beside the aggregate that lives it:

```csharp
// src/Modules/Sales/Domain/SalesOrders/SalesOrderStatus.cs
namespace Erp.Modules.Sales.Domain;

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
                 submit                            approve
   ┌─────────┐  ──────────►  ┌─────────────────┐  ──────────►  ┌──────────┐
   │  Draft  │               │ PendingApproval │               │ Approved │
   └────┬────┘               └───┬─────────┬───┘               └────┬─────┘
        │                        │         │                        │
        │                reject  │         │                        │
        │                        ▼         │                        │
        │                  ┌────────────┐  │                        │
        │                  │  Rejected  │  │                        │
        │                  │  (final)   │  │                        │
        │                  └────────────┘  │                        │
        │                                  │                        │
        │ cancel                           │ cancel                 │ cancel
        ▼                                  ▼                        ▼
   ┌──────────────────────────────────────────────────────────────────────┐
   │                        Cancelled  (final)                            │
   └──────────────────────────────────────────────────────────────────────┘
```

*Reserved stock is released on the way into `Cancelled`/`Rejected`; the balance charged at `Approved` is
reversed if an approved order is later cancelled. The diagram is the "what's legal"; the handlers
([Phases 7–8](07-submit-reserve-concurrency.md)) attach the side effects to each arrow.*

```csharp
// src/Modules/Sales/Domain/SalesOrders/SalesOrderStatusRules.cs
namespace Erp.Modules.Sales.Domain;

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

> 🔑 **Why a lookup table beats scattered `if`s.** Every legal move sits in one readable grid you can check
> against the business's own state diagram. Spread the same rules across `if` statements in five methods and
> nobody can answer "can an Approved order be cancelled?" without reading all five.

## 5. The `ItemCategory` rules (restrictions + limits)

A small domain type carrying the category rules from the schema:

```csharp
// src/Modules/Sales/Domain/ItemCategories/ItemCategory.cs
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
restrictions. The handler will hand the caller's role to `EnsureRoleAllowed`; the *fact* (the role) comes
from outside, the *judgement* lives here.

That split is worth naming, because it's the shape every domain method in this course takes: **pass in facts,
never decisions.** `EnsureRoleAllowed("SalesRep")` passes a fact — *this is who's calling* — and the category
decides. Had we written `EnsureRoleAllowed(bool callerIsAllowed)`, the caller would be doing the deciding and
the category would just be nodding along; the rule would have quietly moved out of the domain, and a second
caller could nod differently. Watch for that inversion — a `bool` parameter on a domain method is almost
always a decision that escaped.

## 6. The `SalesOrder` aggregate

This is the centerpiece. Read the comments — every design choice is deliberate.

```csharp
// src/Modules/Sales/Domain/SalesOrders/SalesOrder.cs
using Erp.Shared.BuildingBlocks;   // AggregateRoot<TId>, DomainException

namespace Erp.Modules.Sales.Domain;

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

    // The approval threshold is the ORDER'S own knowledge (a surfaced high-value flag — see §8).
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
> at the moment we raise `SalesOrderCreated(order.Id, …)`, and it stays `0` forever, because nothing ever
> writes the real id back onto the in-memory aggregate. Nothing in this course *consumes* that event, so the
> zero is harmless — but the trap is real: **never assume `SalesOrderCreated.Id` is usable.**
>
> Two honest fixes, if you ever add a handler for it. **(a)** Leave the id *out* of the created event —
> `SalesOrderCreated(CustomerId, CreatedBy)` says everything true at that moment. **(b)** Give the aggregate a
> way to *receive* its id after the `INSERT` and raise the event then. Note that (b) is more work than it
> looks: `Id` comes from `Entity<TId>` with a `protected set`, so a repository — which isn't a subclass —
> physically cannot assign it. You'd have to add a seam for it (an `internal void AssignId(SalesOrderId id)`
> on the aggregate, say). That's exactly why this course takes the simpler road instead: the repository's
> `AddAsync` **returns** the new id, and the handler returns *that* ([Phase 6](06-create-order.md)). We never
> pretend the aggregate knows an id it was never given.

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
  itself. **Guards before mutation** is the discipline: validate everything *first*, change state *after*,
  so a throw can never leave the object stranded in a half-updated, broken state.
- **(c)** Record the fact. The *inventory reservation* is **not** done here — that's an
  atomicity-under-concurrency concern the handler + SQL own (Phase 7). The domain *expresses* "this got
  submitted"; it does not reach into a database. Keep that boundary sharp.

> ❓ **You might be wondering: "Why doesn't `Submit()` reserve the inventory right here? It's part of
> submitting."** Because reserving stock safely requires an *atomic database operation* — and the domain
> is forbidden from touching the database (it must stay pure and unit-testable). The domain says "a
> submit happened"; the **handler** orchestrates the actual reservation via SQL inside a transaction.
> This is exactly the "policy in the domain, atomicity in SQL" division. If you put a DB call in `Submit()`,
> you couldn't unit-test the state machine without a database — and you'd have broken the dependency rule.

### The `SalesOrderLine`

```csharp
// src/Modules/Sales/Domain/SalesOrders/SalesOrderLine.cs
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
// src/Modules/Sales/Domain/Customers/Customer.cs
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
                $"Order total ₱{orderTotal.Amount:N2} would exceed the credit limit. " +
                $"Available credit: ₱{(CreditLimit.Amount - OutstandingBalance.Amount):N2}.");
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
> the credit limit.** So a customer with ₱500 of credit left could, in theory, create *ten* separate
> orders that each individually fit under ₱500 — because none of them has been approved yet, so none has
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
// src/Modules/Sales/Domain/SalesOrders/SalesOrderEvents.cs
// IDomainEvent already lives in Erp.Shared.BuildingBlocks (Phase 1), next to Entity/AggregateRoot —
// every module's events reuse it. We do NOT redeclare it here; we just implement it.
public sealed record SalesOrderCreated(SalesOrderId Id, CustomerId CustomerId, UserId CreatedBy) : IDomainEvent;
public sealed record SalesOrderSubmitted(SalesOrderId Id, CustomerId CustomerId, Money Total) : IDomainEvent;
public sealed record SalesOrderApproved(SalesOrderId Id, CustomerId CustomerId, Money Total, UserId Approver) : IDomainEvent;
public sealed record SalesOrderRejected(SalesOrderId Id) : IDomainEvent;
public sealed record SalesOrderCancelled(SalesOrderId Id) : IDomainEvent;
```

```csharp
// src/Modules/Sales/Domain/SalesOrders/ApprovalPolicy.cs
public static class ApprovalPolicy
{
    public static readonly Money Threshold = new(50_000m);  // orders over ₱50,000 are flagged high-value
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

> ❓ **"Couldn't `Submit` just take an `approvalGranted` flag?"** This is §5's *"pass in facts, never
> decisions"* rule showing up again, and it's worth seeing it bite twice. A `bool` handed to `Submit` means
> **the caller asserts** the approval happened; nothing in the system ever checks that a manager was actually
> involved. Splitting approval into its own role-gated `Approve` step makes the manager's involvement a fact
> the system *enforces* rather than a claim it *trusts*. That's the shape real ERPs use, and it's why
> [Phase 8](08-approval-cancellation.md) exists.

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

## 10. Unit tests — write them *now*, alongside the model

Here's the payoff of a pure domain: **you can test every rule right now, with zero infrastructure.** No
database, no web host, no mocks — just `new` up an object and assert. This is the ideal moment to start
testing, while the rules are fresh, so we do it here rather than saving all testing for the end.

### Create the test project and install the tools

This is the first phase that needs a test project, so it's where we create it and add the test packages
(the "install only when needed" rule from Phase 1):

```bash
dotnet new xunit -n Erp.Modules.Sales.UnitTests -o tests/Erp.Modules.Sales.UnitTests
dotnet sln add tests/Erp.Modules.Sales.UnitTests
dotnet add tests/Erp.Modules.Sales.UnitTests reference src/Modules/Sales/Erp.Modules.Sales.csproj
dotnet add tests/Erp.Modules.Sales.UnitTests package FluentAssertions
```

(CPM adds a `<PackageVersion>` line for `FluentAssertions`, and — because the test project references the
*module*, not the other way around — the module's Domain stays free of any test dependency.)

> 📌 **The `xunit` template fights Central Package Management. Fix it before you build.** `dotnet new xunit`
> writes its four packages **with versions baked in**, which is exactly what [Phase 1 §5b](01-solution-setup.md)
> forbade. Build now and you get:
>
> ```
> error NU1008: The following PackageReference items cannot define a value for Version:
>               coverlet.collector, Microsoft.NET.Test.Sdk, xunit, xunit.runner.visualstudio
> ```
>
> This isn't a mistake on your part — the template simply predates CPM. The fix is the one §5b already
> taught: **move each version out of the `.csproj` and into `Directory.Packages.props`.** Strip the
> `Version="…"` attribute from all four lines in `tests/Erp.Modules.Sales.UnitTests/…csproj`:
>
> ```xml
> <PackageReference Include="coverlet.collector" />
> <PackageReference Include="Microsoft.NET.Test.Sdk" />
> <PackageReference Include="xunit" />
> <PackageReference Include="xunit.runner.visualstudio" />
> ```
>
> …and add the versions the template chose (yours may be newer — use whatever it generated) to
> `Directory.Packages.props`:
>
> ```xml
> <!-- Testing -->
> <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
> <PackageVersion Include="xunit" Version="2.9.3" />
> <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.4" />
> <PackageVersion Include="coverlet.collector" Version="6.0.4" />
> ```
>
> Do the same the next time you scaffold a test project ([Phase 12](12-testing.md) adds the integration one).
> Annoying once, then never again — and it's a fair price for having every version in a single file.

### The tests (`tests/Erp.Modules.Sales.UnitTests/SalesOrderTests.cs`)

```csharp
// tests/Erp.Modules.Sales.UnitTests/SalesOrderTests.cs
using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;   // DomainException
using Xunit;

public class SalesOrderTests
{
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
        order.AddLine(new ItemId(1), new Quantity(1), new Money(60_000));   // > ₱50,000 threshold
        Assert.True(order.RequiresApproval);   // surfaced so a manager UI can prioritise; see the ⚠️ note above
    }

    [Fact]
    public void Blocked_customer_is_rejected()
    {
        var c = new Customer(new CustomerId(3), "Gamma", isBlocked: true, new Money(20_000), Money.Zero);
        Assert.Throws<DomainException>(() => c.EnsureNotBlocked());
    }
}
```

> 💡 Try adding one yourself: assert that `Draft → Approved` (skipping submit) throws, or that `Total`
> equals the sum of two lines. Each is three lines and needs no setup — that ease *is* the reward for a pure
> domain.

### Checkpoint — green tests prove the rules ✅

```bash
dotnet test tests/Erp.Modules.Sales.UnitTests
```

All tests pass? Then **Phase 5 is done and verifiable**: the aggregate defends its own invariants, and you
have executable proof of it. (Phase 12 later adds *integration* tests that go through the real HTTP + SQL
pipeline — a different level. These pure-domain tests are the fast, foundational layer.)

## 11. Common mistakes & best practices

- ❌ **Public setters "for the mapper."** ✅ Private setters + `Create` factory + a private constructor
  the repository uses to rehydrate. Public setters reopen the dumb-data-bag hole.
- ❌ **Storing `Total` as a settable field.** ✅ Compute it from the lines; it can't drift. (We *also*
  persist a snapshot in the DB for fast search — the domain's computed value is the authority; the DB
  column is a display convenience. [Phase 10](10-queries-search.md) leans on that distinction.)
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

---

← Previous: [Phase 4 (Part 5 of 5) — Request Flow, Checkpoint & Common Mistakes](04e-jwt-wrap-up.md) · [Tutorial overview](00-START-HERE.md) · Next: [Phase 6 — The Create Sales Order slice](06-create-order.md) →
