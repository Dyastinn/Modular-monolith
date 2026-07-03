# Phase 5 — The Hard Part: Inventory, "Two People at Once," Pricing & Booking

> 📄 Expert version: [`../plan-Opus/06-phase-5-inventory-concurrency.md`](../plan-Opus/06-phase-5-inventory-concurrency.md)

**In one sentence:** This is where the architecture *earns its keep* — we handle reserving stock
safely when two people order at the same moment, and we learn the golden rule: *policy goes in the
code, safety-under-simultaneous-access goes in the database.*

**What you'll build:** The `BookOrder` feature, inventory reservation/deduction stored procedures with
atomic guards, optimistic-concurrency protection via `rowversion`, a `PricingService`, and the
approval flow.

**Before you start:** Phase 4 complete (you can create and fetch an order).

> This phase is the payoff for the whole project. If any single thing teaches "where does logic go?",
> it's inventory reservation. Spend real time on Step 3.

---

## Step 1 — Each operation and where its rule lives

| Operation | Who owns the rule | Why |
|---|---|---|
| Pricing (line prices, total) | **Domain / domain service** | Pure calculation; must be testable without a database. |
| Approval required over threshold | **Domain** (`Order.RequiresApproval`) | A rule about the order's own state. |
| Inventory *availability* (just informational) | **Domain expresses it** | Fast feedback, testable. |
| Inventory *reservation* (the real guard) | **Database, atomic** | Simultaneous access makes an in-memory check unsafe. ← the big lesson |
| Stock deduction on booking | **Database, atomic, in the transaction** | Same reason; must be consistent with the status change. |
| Status change Draft→Booked | **Domain** (`Order.Book`) | State-machine rule; the entity owns it. |
| "Don't overwrite someone else's change" | **Database** (version check) + domain surfaces the conflict | The check must be atomic at write time. |

## Step 2 — Pricing: our first *justified* domain service

Back in Phase 3 we said domain services are rare and you should resist creating them. So why does
pricing get one? Because pricing needs data from *more than one* place (materials, price lists) and
doesn't belong to `Order` alone. **That** is the specific situation a domain service is for.

```csharp
public interface IPricingService
{
    Task<Money> GetUnitPrice(MaterialId material, CancellationToken ct);
    // Could grow later: volume discounts, customer-specific price lists.
}
```

> 🔑 **The test for "should this be a service?":** *A domain service exists only when the logic spans
> multiple aggregates, or needs data the aggregate doesn't own.* If pricing were just "unit price ×
> quantity" using data already on the order, it'd be a method on `OrderLine` — **not** a service.
> It becomes a service *only because* it reaches outside the order for price-list data. Say that test
> out loud every time you're tempted to add a service — otherwise you're back to the dumb-data-bag
> trap.

> Keep `IPricingService` honest: it *computes prices*. It does not book orders or touch inventory. One
> job. A service that starts coordinating steps is secretly a handler.

## Step 3 — Inventory reservation: THE concurrency lesson (the important one)

### The naive version (and why it's broken)

```csharp
// ❌ BROKEN when two people act at once:
var inv = await inventory.Get(materialId);          // read: Available = 5
if (inv.Available >= qty)                            // check in memory: 5 >= 5? yes
    await inventory.Reserve(materialId, qty);        // then write
```

Picture two customers both trying to grab the last 5 units at the same instant. Both requests read
`Available = 5`. Both pass the `if`. Both reserve. **You've now reserved 10 units of a 5-unit stock.**
That's a **race condition**, and here's the crucial insight: *no amount of clean domain code fixes it*,
because the check and the write are two separate steps, and someone can slip in between them.

> 💡 **Analogy:** Two shoppers both see one item on the shelf and both reach for it. Neither noticed the
> other. The "check" (seeing it) and the "take" (grabbing it) weren't a single motion.

### The correct version — one atomic step in the database

The fix: make the check and the decrement **a single statement** that the database runs indivisibly.
The database guarantees only one caller can win:

```sql
CREATE PROCEDURE usp_Inventory_Reserve
    @MaterialId BIGINT,
    @Qty        INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Inventory
    SET Reserved = Reserved + @Qty
    WHERE MaterialId = @MaterialId
      AND (OnHand - Reserved) >= @Qty;   -- the check is PART OF the write
    SELECT @@ROWCOUNT;                    -- 0 = not enough stock; 1 = reserved successfully
END
```

The handler looks at the rowcount: `0` means "not enough stock," which it turns into a business
failure. The domain still *expresses* the availability rule (for validation and unit tests), but the
**authoritative, safe** guard is this atomic SQL.

> 🔑 **The whole point, in one box:**
> - **Policy** ("we only reserve what's available") is a *domain* idea → lives in code, for fast
>   feedback and testing.
> - **Atomicity** ("no two callers can both win the last unit") is a *database* concern → lives in SQL.
>
> Both are needed. They live in different places. **This is not duplication** — it's the same rule
> enforced at two levels for two *different* reasons: fast feedback vs. correctness under simultaneous
> access. *(Turnstile analogy: the sign says "one at a time" — policy; the physical turnstile actually
> enforces it — atomicity.)*

### Where reservation happens

Reservation is part of **booking** and runs *inside the booking transaction*, so that "stock reserved"
and "order booked" either both commit or both roll back — never one without the other.

## Step 4 — BookOrder, all together

```csharp
public sealed record BookOrderCommand(long OrderId, bool ApprovalGranted) : ICommand<Unit>;

public sealed class BookOrderHandler(
    IOrderRepository orders,
    IInventoryService inventory,
    IUnitOfWork uow) : IRequestHandler<BookOrderCommand, Unit>
{
    public async Task<Unit> Handle(BookOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetAsync(new OrderId(cmd.OrderId), ct)
            ?? throw new NotFoundException($"Order {cmd.OrderId} not found.");

        // DOMAIN decides IF booking is legal (draft? non-empty? approved if needed?). Throws if not.
        order.Book(cmd.ApprovalGranted);

        await using var tx = await uow.BeginAsync(ct);

        // DATABASE makes each reservation safe under concurrency. Any failure → throw → rollback.
        foreach (var line in order.Lines)
        {
            var reserved = await inventory.TryReserve(line.MaterialId, line.Quantity, tx, ct);
            if (!reserved)
                throw new DomainException($"Insufficient stock for material {line.MaterialId.Value}.");
        }

        await orders.UpdateAsync(order, tx, ct);   // save Booked status + version check (Step 5)
        await tx.CommitAsync(ct);

        return Unit.Value;   // OrderBooked event dispatched after commit (Phase 7)
    }
}
```

Look at the clean division of labor — **three responsibilities, three homes, no overlap:**

- The **domain** decides *whether* booking is legal (`order.Book`).
- The **database** guarantees the reservation is *safe* when people act simultaneously.
- The **handler** owns the *transaction boundary* that makes them commit together.

*(`Unit` is a tiny "nothing meaningful to return" type; define it in the messaging abstractions.)*

## Step 5 — Protecting against "two managers editing the same order"

Two managers open order #42, both make changes, both save. Without protection, the second save silently
wipes out the first — a **lost update**. We guard with a `rowversion` column (a hidden stamp the
database bumps on every change) and a conditional update:

```sql
CREATE PROCEDURE usp_Order_UpdateStatus
    @OrderId    BIGINT,
    @Status     INT,
    @RowVersion BINARY(8)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.[Order]
    SET Status = @Status
    WHERE OrderId = @OrderId AND RowVersion = @RowVersion;   -- only if nobody changed it since we read it
    SELECT @@ROWCOUNT;   -- 0 => someone else already changed it
END
```

Rowcount `0` → the repository throws a `ConcurrencyException` → the handler lets it bubble up (the
caller reloads and retries). The domain doesn't manage the `rowversion` — that's a persistence detail
the repository loads with the order and passes back on update.

> 🔑 **The honest boundary here:** the concurrency *token* (`rowversion`) is infrastructure, but the
> *decision to fail on conflict* is a business choice you surface as a meaningful exception.
> *(Contract analogy: "I'll only sign if this is still the exact copy I read.")*

## Step 6 — Reserve vs. deduct vs. release (three related moves)

Model the real two-step lifecycle explicitly:

- **Reserve** on booking — holds the stock so it can't be double-sold.
- **Deduct** `OnHand` on completion/fulfillment (Phase 7's `Completed` transition) — the stock actually
  leaves.
- **Release** the reservation on cancellation — give it back.

Each is an atomic SQL statement of the same shape as Step 3. **Write your policy down** so the procs
stay consistent. This plan uses: *reserve-on-book, deduct-on-complete, release-on-cancel.*

## What to produce in this phase

- `Unit` type; `BookOrderCommand` + validator + `BookOrderHandler`.
- `IInventoryService`/repository with atomic `TryReserve`, `Deduct`, `Release`, backed by
  `usp_Inventory_Reserve`, `usp_Inventory_Deduct`, `usp_Inventory_Release` (all guard via rowcount).
- `IPricingService` + implementation; wire it into `CreateOrder` and booking totals.
- A `rowversion` column on `Order`; `usp_Order_UpdateStatus` with the version guard;
  `ConcurrencyException`.
- `POST /orders/{id}/book` endpoint.
- Unit tests: approval threshold, illegal booking states. (The concurrency *race* integration test can
  wait for Phase 8 — but note it now.)

**NOT now:** queries/read models (Phase 6), the full CancelOrder flow and event dispatch (Phase 7).
Keep this phase about *write-side rules and concurrency*.

## Common mistakes to avoid

- **Check-then-act in C# for inventory.** It looks fine in tests (which have no concurrency) and breaks
  in production. The guard *must* be atomic in SQL.
- **A "manager" domain service that orchestrates the booking.** That's the handler's job. Domain
  services *compute/decide*; they don't run transactions or coordinate repositories.
- **Putting the `rowversion` check in the domain.** The mechanism is infrastructure; only the
  *conflict outcome* is domain-meaningful.
- **Forgetting to release reservations on cancel** → phantom stock shortages. Always model
  reserve/deduct/release as a set.

---

## ✅ Check Yourself

1. **Why does the naive in-memory availability check fail, and what *exactly* makes the SQL version
   safe?** *(Hint: check-and-write as two steps vs. one atomic step.)*
2. **Is "reserve only if available" duplicated between the domain and SQL? Explain why it's *not*
   duplication.**
3. **Why is `IPricingService` a legitimate domain service, and what would make it *not* one?**
4. **Who owns the transaction boundary in `BookOrder`, and why not the domain or the repository?**
5. **Where does the `rowversion` token live conceptually, and who decides to fail on a conflict?**
