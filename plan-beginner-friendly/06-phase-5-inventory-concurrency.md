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

---

## Step 1 — Inventory is its own module (and Sales may NOT reach into it)

Reserving stock is not the Sales department's job — it's **Inventory's**. In a modular monolith, that means
Inventory is *its own module*, with its own domain, its own `inventory` database schema, and its own
tables. Sales does not own stock, does not see the `inventory.Stock` table, and cannot `using
Erp.Modules.Inventory.Domain`.

So how does Sales get stock reserved? Through a **published contract** — a small interface that lives in the
Shared kernel, that Inventory *implements* and Sales *calls*:

```csharp
// Erp.Shared.Common/Contracts/IStockReservation.cs  (the ONLY thing Sales may use to touch stock)
namespace Erp.Shared.Contracts;

public interface IStockReservation
{
    Task<bool> TryReserveAsync(long productId, int quantity, IDbTransaction tx, CancellationToken ct);
    Task ReleaseAsync(long productId, int quantity, IDbTransaction tx, CancellationToken ct);
}
```

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
- **Split-later stays cheap.** The day Inventory becomes its own service, you swap
  `StockReservationService` for an HTTP/queue client behind the *same* `IStockReservation` interface. Sales
  doesn't change.

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

## Step 2 — Pricing: our first *justified* domain service (inside Sales)

Back in Phase 3 we said domain services are rare. So why does pricing get one? Because pricing needs data
from *more than one* place (products, price lists) and doesn't belong to `SalesOrder` alone. **That** is the
specific situation a domain service is for.

```csharp
public interface IPricingService
{
    Task<Money> GetUnitPrice(ProductId product, CancellationToken ct);
    // Could grow later: volume discounts, customer-specific price lists.
}
```

> 🔑 **The test for "should this be a service?":** *A domain service exists only when the logic spans
> multiple aggregates, or needs data the aggregate doesn't own.* If pricing were just "unit price × quantity"
> using data already on the order, it'd be a method on `SalesOrderLine` — **not** a service. It becomes a
> service *only because* it reaches for price-list data. Say that test out loud every time you're tempted.

> 🧭 **A note for later:** in a full ERP, product catalog and pricing might themselves be a module. We keep
> `IPricingService` inside Sales here to keep the phase focused on *one* new module (Inventory). Introducing
> a second cross-module contract is left as an exercise once the pattern clicks.

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

        await orders.UpdateAsync(order, tx, ct);   // save Submitted status + version check (Step 5)
        await tx.CommitAsync(ct);

        return Unit.Value;   // SalesOrderSubmitted event dispatched after commit (Phase 7)
    }
}
```

The clean division of labor — **four responsibilities, four homes, no overlap:**

- The **Sales domain** decides *whether* submitting is legal (`order.Submit`).
- The **Inventory module** guarantees the reservation is *safe* under concurrency (its atomic proc).
- The **contract** (`IStockReservation`) is the only channel between them — the wall with a doorway.
- The **handler** owns the *transaction boundary* that makes them commit together.

*(`Unit` is a tiny "nothing meaningful to return" type; define it in `Shared.Messaging`.)*

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
reloads and retries). The domain doesn't manage the `rowversion` — that's a persistence detail the
repository loads with the order and passes back on update.

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
