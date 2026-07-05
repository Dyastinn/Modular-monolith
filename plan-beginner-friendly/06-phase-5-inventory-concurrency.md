# Phase 5 — The Hard Part: a Second Module, "Two People at Once," and Submitting

> 📄 Expert version: [`../plan-Opus/06-phase-5-inventory-concurrency.md`](../plan-Opus/06-phase-5-inventory-concurrency.md)

**In one sentence:** This is where the architecture *earns its keep* twice over — we add a **second
module** (Inventory) and make Sales talk to it *across the wall*, and we reserve stock safely when two
people order at the same moment, learning the golden rule: *policy goes in the code,
safety-under-simultaneous-access goes in the database.*

**What you'll build:** The **Inventory module**, a published `IStockReservation` contract so Sales can
reserve without reaching into Inventory, the `SubmitSalesOrder` feature, atomic reservation/deduction
stored procedures, optimistic-concurrency protection via `rowversion`, a `PricingService`, and the
approval-threshold flow.

**Before you start:** Phase 4 complete (you can create and fetch a sales order).

> This phase is the payoff for the whole project. If any single thing teaches "where does logic go?", it's
> inventory reservation. Spend real time on Step 3 — and on Step 1, which is the modular-monolith heart.

> 🧭 **This is the biggest phase — so do it in two sittings.** It bundles two genuinely separate lessons.
> Don't try to swallow both at once:
> - **Part A — a second module + the wall between them (Steps 1–2).** Build the Inventory module and the
>   `IStockReservation` contract. *Stop at the Part A checkpoint, make it compile, take a break.*
> - **Part B — concurrency: doing it safely when two people act at once (Steps 3–6).** The atomic SQL
>   reservation, `rowversion`, and the submit flow that ties it together.
>
> Each part is about a 6-out-of-10 in difficulty on its own; mashed together they feel like a 9. Splitting
> them is the trick to not drowning.

---

# ── PART A — A Second Module and the Wall Between Them ──

## Step 1 — Inventory is its own module (and Sales may NOT reach into it)

Reserving stock is not the Sales department's job — it's **Inventory's**. In a modular monolith, that means
Inventory is *its own module*, with its own domain, its own `inventory` database schema, and its own
tables. Sales does not own stock, does not see the `inventory.Stock` table, and cannot `using
Erp.Modules.Inventory.Domain`.

So how does Sales get stock reserved? Through a **published contract** — a small interface that lives in the
Shared kernel, that Inventory *implements* and Sales *calls*:

```csharp
// Erp.Shared.Common/Contracts/IStockReservation.cs  (the ONLY thing Sales may use to touch stock)
namespace Erp.Shared.Common.Contracts;   // namespace matches the folder: Erp.Shared.Common → .Contracts

public interface IStockReservation
{
    Task<bool> TryReserveAsync(long productId, int quantity, IDbTransaction tx, CancellationToken ct);  // hold stock (submit)
    Task ReleaseAsync(long productId, int quantity, IDbTransaction tx, CancellationToken ct);           // give it back (cancel)
    Task DeductAsync(long productId, int quantity, IDbTransaction tx, CancellationToken ct);            // ship it (complete) — Phase 7
}
```

> ℹ️ **Three methods, one lifecycle.** We show all three now so the contract is complete, even though you
> only *use* `TryReserveAsync` in this phase. `ReleaseAsync` is used on cancel and `DeductAsync` on complete —
> both in [Phase 7](08-phase-7-remaining-slices.md). They're the *reserve → (deduct | release)* set from
> Step 6; each is one atomic SQL statement of the same shape.

```
   Sales module                 Shared.Contracts            Inventory module
   ────────────                 ────────────────            ────────────────
   SubmitSalesOrderHandler ───► IStockReservation  ◄─────── StockReservationService (implements it)
        (calls the interface)     (the contract)              (owns inventory.Stock + the atomic proc)
```

Neither module references the other. Sales depends on the **interface** (in Shared); Inventory provides the
**implementation**; the Api wires the two together in the container. This is the whole modular-monolith
payoff:

- **The wall is real.** Sales physically cannot bypass the contract to poke Inventory's tables — there's no
  reference and no `using` that would compile.
- **Split-later stays cheap** — *cheaper*, honestly, not free. The day Inventory becomes its own service,
  Sales still reserves stock through a small interface it doesn't implement, and its *call site* barely
  moves. But be precise about what changes: our `IStockReservation` takes an `IDbTransaction` so the two
  can share **one** database transaction (fine inside a single-database monolith). You **cannot send a
  database transaction over a network**, so once Inventory has its own database, that parameter has to go,
  and "reserve in the same transaction" becomes "reserve as a separate step, and *release* if the rest
  fails" — a **saga** with compensation. So extraction reshapes the *contract* and adds saga logic; what it
  *doesn't* do is force you to untangle Sales from Inventory's tables and types, because those walls were
  never crossed. That's the real, still-large payoff. (The next note, and Phase 9, go deeper.)

> ❓ **"Why put the contract in Shared instead of in the Inventory module?"** Because if Sales referenced the
> Inventory *project* to get the interface, it could also see Inventory's internals — the wall would leak.
> Putting the tiny, stable contract in Shared lets *both* modules depend on it while depending on *nothing*
> of each other's. (At larger scale, each module gets its own `*.Contracts` project; for one contract, a
> shared `Contracts` folder is the right amount of ceremony — see Phase 9.)

> ❓ **"They share the handler's database transaction — isn't that violating module ownership?"** Honest
> answer: in a *monolith with one database*, sharing a DB transaction for one atomic operation is allowed
> and pragmatic — the boundary we're enforcing is over *code* and *schema ownership* (Inventory owns the
> `inventory` schema; Sales writes it only through the contract). If Inventory later moves to its own
> database/service, this synchronous same-transaction call becomes an integration event + compensation
> (a "saga") — which is exactly the trade-off Phase 9 discusses. We take the simple correct option now and
> name the upgrade path.

## Step 2 — Pricing: a read *port*, and what a *real* domain service would be

Pricing needs data the order doesn't own (a price list), so `SalesOrder` can't compute it alone. We express
that need as a small interface the Sales domain *declares* and Infrastructure *implements*:

```csharp
public interface IPricingService
{
    Task<Money> GetUnitPrice(ProductId product, CancellationToken ct);
    // Could grow later: volume discounts, customer-specific price lists.
}
```

> ⚠️ **Naming correction (important — earlier drafts called this a "domain service"; it isn't one).** Because
> `IPricingService` *fetches data from a price list*, it's really a **read port — data access** — the same
> shape as a repository (declared by the domain, implemented in Infrastructure with Dapper/SQL). A DDD
> **domain service** is something else: **stateless domain *logic* that spans several aggregates and does no
> I/O.** The tell is I/O: anything that reads a database or calls another system is a *port/adapter*, not a
> domain service. `IPricingService` reads the price list → port. (The name has "Service" in it, but the name
> isn't what decides the category — the *behavior* does.)
>
> **What a genuine domain service looks like** (for contrast): imagine a rule like *"spread a whole-order
> discount across its lines proportionally to each line's value."* That's pure calculation over data already
> on the aggregate(s), touches no database, and doesn't sit naturally on any single line — so it'd be a
> stateless `IOrderDiscountPolicy.Apply(order)` domain service. Notice: **no I/O.** That's the difference.
> This project doesn't need one; we're just naming the shape so you can recognize it.

> 🧭 **A note for later:** in a full ERP, product catalog and pricing might themselves be a module. We keep
> `IPricingService` inside Sales here to keep the phase focused on *one* new module (Inventory). Introducing
> a second cross-module contract is left as an exercise once the pattern clicks.

> ### ✋ Part A checkpoint — stop here and breathe
>
> Before touching concurrency, make sure Part A stands on its own:
> - The **Inventory module** exists as its own project, with its own `inventory` schema.
> - `IStockReservation` lives in **Shared**, Inventory **implements** it, and Sales depends only on the
>   *interface* — neither module references the other (the compiler enforces it).
> - You can explain, out loud, *why* the contract goes in Shared and not in the Inventory project.
>
> If those hold, you've already learned the modular-monolith heart of this phase. **Part B is a different
> lesson** — what "safe" means when two people click at the same instant. Come back to it fresh.

---

# ── PART B — Doing It Safely When Two People Act at Once ──

## Step 3 — Inventory reservation: THE concurrency lesson (the important one)

This logic lives **inside the Inventory module**, behind the `IStockReservation` contract. Sales never sees
it — but you need to understand it, because it's the heart of the project.

### The naive version (and why it's broken)

```csharp
// ❌ BROKEN when two people act at once:
var stock = await stockRepo.Get(productId);          // read: Available = 5
if (stock.Available >= qty)                           // check in memory: 5 >= 5? yes
    await stockRepo.Reserve(productId, qty);          // then write
```

Picture two customers both grabbing the last 5 units at the same instant. Both read `Available = 5`. Both
pass the `if`. Both reserve. **You've now reserved 10 units of a 5-unit stock.** That's a **race condition**,
and here's the crucial insight: *no amount of clean domain code fixes it*, because the check and the write
are two separate steps, and someone can slip in between them.

### Watch it happen, moment by moment

Stock starts at `Available = 5`. Request A and Request B each want 5 units:

```
 time   Request A                          Request B
 ────   ────────────────────────────────   ────────────────────────────────
  t1    read Available → 5
  t2                                        read Available → 5      ← still 5! A hasn't written yet
  t3    5 >= 5?  yes ✓
  t4                                        5 >= 5?  yes ✓          ← both passed the check
  t5    reserve 5  (write)
  t6                                        reserve 5  (write)      ← reserved 10 of 5 💥
```

Stare at **t2**: B reads the stock *after* A read it but *before* A wrote. Both checks passed against stale
information. Nothing in the C# is "buggy" in the usual sense — each request, alone, is correct. The bug is
purely in the *timing overlap*, and that's why it's sneaky: **it never shows up when you test one request at
a time.** It only appears under real simultaneous load, in production, often intermittently.

> 💡 **Analogy:** Two shoppers both see one item on the shelf and both reach for it. The "check" (seeing it)
> and the "take" (grabbing it) weren't a single motion.

### The correct version — one atomic step in the database

Make the check and the decrement **a single statement** the database runs indivisibly:

```sql
CREATE PROCEDURE inventory.usp_Stock_Reserve
    @ProductId BIGINT,
    @Qty       INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE inventory.Stock
    SET Reserved = Reserved + @Qty
    WHERE ProductId = @ProductId
      AND (OnHand - Reserved) >= @Qty;   -- the check is PART OF the write
    SELECT @@ROWCOUNT;                    -- 0 = not enough stock; 1 = reserved successfully
END
```

### Why this SQL is safe when the C# wasn't

The magic is in *one word*: this is a **single** `UPDATE` statement. The database runs each `UPDATE` against
a row as an indivisible unit — it briefly locks the row so no other statement can touch it mid-operation. So
the check and the change happen together, with no gap. Replay the timeline (`OnHand=5, Reserved=0`):

```
 time   Request A                              Request B
 ────   ────────────────────────────────────  ────────────────────────────────────
  t1    UPDATE ... WHERE (5-0) >= 5  → matches
        Reserved becomes 5, rowcount = 1 ✓
  t2                                           UPDATE ... WHERE (5-5) >= 5  → 0 >= 5 is FALSE
                                               nothing updated, rowcount = 0 ✗
```

B checks against the *already-updated* value and correctly finds nothing left → rowcount `0` → "insufficient
stock." **Exactly one winner. No overselling. No luck involved.**

> ❓ **"So should ALL my logic go in SQL then, to be safe?"** No — the exact trap Phase 0 warned about. Put
> *only* the atomicity-critical guard in SQL. Everything else — "only draft orders can be submitted," "over
> $50k needs approval" — stays in the domain where it's testable. Rule of thumb: **ask "does
> two-people-at-once break the in-memory check?" If yes → SQL. If no → domain.** Reservation is a "yes."
> Almost everything else is a "no."

> 🔑 **The whole point, in one box:**
> - **Policy** ("we only reserve what's available") is a *domain* idea → lives in code, for fast feedback
>   and testing.
> - **Atomicity** ("no two callers can both win the last unit") is a *database* concern → lives in SQL.
>
> Both are needed. **This is not duplication** — it's the same rule enforced at two levels for two
> *different* reasons: fast feedback vs. correctness under simultaneous access. *(Turnstile: the sign says
> "one at a time" — policy; the physical turnstile enforces it — atomicity.)*

## Step 4 — SubmitSalesOrder, all together (across the module wall)

The Sales feature `SubmitSalesOrder` decides *whether* submitting is legal (domain), then asks Inventory to
reserve (contract), all inside one transaction the handler owns:

```csharp
// Modules/Sales/Features/SubmitSalesOrder/SubmitSalesOrderHandler.cs
public sealed record SubmitSalesOrderCommand(long SalesOrderId, bool ApprovalGranted) : ICommand<Unit>;

public sealed class SubmitSalesOrderHandler(
    ISalesOrderRepository orders,
    IStockReservation stock,       // ← the published Inventory contract, NOT Inventory's internals
    IUnitOfWork uow) : ICommandHandler<SubmitSalesOrderCommand, Unit>
{
    public async Task<Unit> Handle(SubmitSalesOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetAsync(new SalesOrderId(cmd.SalesOrderId), ct)
            ?? throw new NotFoundException($"Sales order {cmd.SalesOrderId} not found.");

        // DOMAIN decides IF submitting is legal (draft? non-empty? approved if needed?). Throws if not.
        order.Submit(cmd.ApprovalGranted);

        await using var tx = await uow.BeginAsync(ct);

        // INVENTORY (across the wall) makes each reservation safe under concurrency.
        foreach (var line in order.Lines)
        {
            var reserved = await stock.TryReserveAsync(
                line.ProductId.Value, line.Quantity.Value, tx.Inner, ct);
            if (!reserved)
                throw new DomainException($"Insufficient stock for product {line.ProductId.Value}.");
        }

        await orders.UpdateAsync(order, tx.Inner, ct);   // save Submitted status + version check (Step 5)
        await tx.CommitAsync(ct);

        return Unit.Value;   // SalesOrderSubmitted event dispatched after commit (Phase 7)
    }
}
```

*(`tx.Inner` is the raw `IDbTransaction` from the `IAppTransaction` we defined in
[Phase 4 §7](05-phase-4-create-order.md). Every write here — the reservation contract call **and**
`orders.UpdateAsync` — runs on that **one** transaction, so they commit or roll back together.)*

> ⚠️ **Reminder: `ApprovalGranted` here is a self-asserted flag, not a real approval.** The command carries
> `bool ApprovalGranted`, and `order.Submit(cmd.ApprovalGranted)` trusts it — but nothing checks a *manager*
> granted it (this course has no auth). So the "$50k needs approval" rule is *modeled* but *unenforced*; a
> caller could pass `true`. That's fine for learning **where the rule lives**, but see the ⚠️ box in
> [Phase 3 §5](04-phase-3-domain.md) and the [business tutorial's role-gated `/approve`](../plan-business-tutorial/08-approval-cancellation.md)
> for how a real system makes approval an authorized, un-fakeable step. *(`Unit` is a tiny "nothing to return"
> type — defined in the next callout.)*

The clean division of labor — **four responsibilities, four homes, no overlap:**

- The **Sales domain** decides *whether* submitting is legal (`order.Submit`).
- The **Inventory module** guarantees the reservation is *safe* under concurrency (its atomic proc).
- The **contract** (`IStockReservation`) is the only channel between them — the wall with a doorway.
- The **handler** owns the *transaction boundary* that makes them commit together.

`Unit` is a tiny "nothing meaningful to return" type — it lets a command that changes state but has no useful
result still fit the `IRequest<TResponse>` shape (you can't write `IRequest<void>`). Define it once in
`Shared.Messaging`:

```csharp
// A stand-in for "void" that still satisfies IRequest<TResponse>.
public readonly record struct Unit
{
    public static readonly Unit Value = default;
}
```

## Step 5 — Protecting against "two managers editing the same order"

Two managers open order #42, both change it, both save. Without protection, the second save silently wipes
the first — a **lost update**. Guard with a `rowversion` column (a hidden stamp the database bumps on every
change) and a conditional update:

```sql
CREATE PROCEDURE sales.usp_SalesOrder_UpdateStatus
    @SalesOrderId BIGINT,
    @Status       INT,
    @RowVersion   BINARY(8)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE sales.SalesOrder
    SET Status = @Status
    WHERE SalesOrderId = @SalesOrderId AND RowVersion = @RowVersion;   -- only if unchanged since we read it
    SELECT @@ROWCOUNT;   -- 0 => someone else already changed it
END
```

Rowcount `0` → the repository throws a `ConcurrencyException` → the handler lets it bubble up (the caller
reloads and retries). The domain doesn't *manage* the `rowversion` — but the aggregate has to *carry* it
between the load and the update, so add one plainly-persistence property to `SalesOrder` that the repository
fills in on load and reads back on save:

```csharp
// on SalesOrder (set by the repository during rehydrate; never touched by business methods)
public byte[]? RowVersion { get; private set; }
```

`GetAsync` reads the `RowVersion` column into it; `UpdateAsync` passes `order.RowVersion` back to the proc as
`@RowVersion`. It's the one honest concession where a persistence detail rides along on the aggregate — keep
it out of every *business* method and it stays harmless. (`ConcurrencyException` is a small custom exception
like `NotFoundException`, in `Erp.Shared.Common`.)

> 🔑 **The honest boundary:** the concurrency *token* (`rowversion`) is infrastructure, but the *decision to
> fail on conflict* is a business choice surfaced as a meaningful exception. *(Contract analogy: "I'll only
> sign if this is still the exact copy I read.")*

## Step 6 — Reserve vs. deduct vs. release (three related moves, all Inventory's)

Model the real two-step lifecycle explicitly, all inside the Inventory module:

- **Reserve** on submit — holds the stock so it can't be double-sold.
- **Deduct** `OnHand` on completion/fulfillment (Phase 7's `Completed` transition) — the stock leaves.
- **Release** the reservation on cancellation — give it back (Phase 7 calls this through the contract).

Each is an atomic SQL statement of the same shape as Step 3. **Write your policy down** so the procs stay
consistent: *reserve-on-submit, deduct-on-complete, release-on-cancel.*

## What to produce in this phase

- The **Inventory module** (`Erp.Modules.Inventory`): its `Domain`, an `inventory` schema with a `Stock`
  table, the atomic `usp_Stock_Reserve`/`_Deduct`/`_Release` procs, and `StockReservationService`
  implementing `IStockReservation`.
- `IStockReservation` contract in `Shared`; `AddInventoryModule()` registers the implementation; the Api
  calls both `AddSalesModule` and `AddInventoryModule`.
- `Unit` type; `SubmitSalesOrderCommand` + validator + handler (two files) calling the contract inside the
  transaction; `POST /sales-orders/{id}/submit`.
- `IPricingService` + implementation inside Sales; wire it into `CreateSalesOrder`.
- A `rowversion` column on `sales.SalesOrder`; `usp_SalesOrder_UpdateStatus` with the version guard;
  `ConcurrencyException`.
- Unit tests: approval threshold, illegal submit states. (The concurrency *race* integration test can wait
  for Phase 8 — but note it now.)

**NOT now:** queries/read models (Phase 6), the full CancelSalesOrder flow and event dispatch (Phase 7).
Keep this phase about *a second module, write-side rules, and concurrency*.

## Common mistakes to avoid

- **Making Sales read or write `inventory.Stock` directly.** Sales goes through `IStockReservation`, always.
  A direct table hit is the wall breaking.
- **Check-then-act in C# for inventory.** Looks fine in tests (no concurrency), breaks in production. The
  guard *must* be atomic in SQL.
- **Putting the reservation contract in the Inventory project.** Then Sales would reference Inventory's
  internals. Contract goes in Shared.
- **A "manager" domain service that orchestrates submitting.** That's the handler's job.
- **Putting the `rowversion` check in the domain.** The mechanism is infrastructure; only the *conflict
  outcome* is domain-meaningful.
- **Forgetting to release reservations on cancel** → phantom shortages. Model reserve/deduct/release as a
  set.

---

## ✅ Check Yourself

1. **Why is Inventory a separate module, and exactly how does Sales reserve stock without referencing it?**
2. **Why does the naive in-memory availability check fail, and what *exactly* makes the SQL version safe?**
3. **Is "reserve only if available" duplicated between the domain and SQL? Explain why it's *not*
   duplication.**
4. **Who owns the transaction boundary in `SubmitSalesOrder`, and why not the domain, the repository, or the
   Inventory module?**
5. **The contract call shares the handler's transaction. Why is that acceptable in a monolith, and what
   would change if Inventory became its own service?**

---

## 👨‍🏫 Answer Key

> This is the hardest phase. If these answers feel heavy, that's expected — re-read Steps 1 and 3.

**1. Why is Inventory a separate module, and how does Sales reserve without referencing it?**
Stock is Inventory's responsibility and data (the `inventory` schema), so it's its own module with its own
walls. Sales reserves through the **`IStockReservation` contract in Shared**: Inventory *implements* it,
Sales *calls* it, and the Api wires them together. Neither module references the other — Sales depends only
on the interface. The wall is enforced by the compiler (no reference = no `using`).

**2. Why does the naive check fail, and what makes the SQL safe?**
The naive version **reads** then **writes** as two steps; two requests can both read "5" before either
writes, so both reserve → 10 of 5 (a race condition). The SQL makes the check and write **one statement**
(`UPDATE ... WHERE (OnHand - Reserved) >= @Qty`), run atomically — one caller at a time, no gap. `@@ROWCOUNT`
reports success (1) or insufficient stock (0).

**3. Is the rule duplicated? Why not?**
No — it's the *same rule at two levels for two reasons*. The **domain** expresses it for fast feedback and
unit testing; the **SQL** enforces it for correctness under simultaneous access. Bad duplication is two
copies that can *drift*; this is one rule deliberately guarded in two places serving different jobs. *(Sign
= policy; turnstile = atomicity.)*

**4. Who owns the transaction boundary, and why not the others?**
The **handler**. Not the **domain** (must stay pure, knows nothing of transactions). Not the **repository**
(the boundary spans multiple operations — reserve every line *and* update status). Not the **Inventory
module** (it only knows how to reserve one product safely; it can't see the whole Sales use case). The
handler is the one place that sees the whole use case, so it sets the atomic boundary.

**5. Why is sharing the transaction acceptable now, and what would change on split?**
In a **monolith with one database**, one DB transaction across a contract call is pragmatic and correct; the
boundary we enforce is code + schema ownership, not separate transactions. If Inventory became its own
**service/database**, you couldn't share a transaction — the reserve would become an **integration event**
with **compensation** (release on failure) — a saga. Same `IStockReservation` seam, different implementation
behind it. That's the split-later path (Phase 9).

> ✅ **You're ready for Phase 6 if:** you can explain why Inventory is a separate module reached only through
> a contract, tell the "last 5 units" race story, and say why domain + SQL guarding the same rule isn't
> duplication. Those are the two most important lessons in the project. 🎯
