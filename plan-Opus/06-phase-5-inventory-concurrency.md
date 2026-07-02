# Phase 5 — Business Rules: Inventory, Concurrency, Pricing, Approval, BookOrder

**Goal:** implement the operations that make the architecture earn its keep — inventory reservation
and stock deduction under concurrency, pricing, the approval rule, and `BookOrder`. This is where
"policy in the domain, atomicity in SQL" (Tension A / A.1) becomes concrete.
**Produces:** `BookOrder` slice, inventory reservation/deduction procs with atomic guards, optimistic
concurrency via `rowversion`, a `PricingService` (domain service — justified), and the approval flow.
**Prereq:** Phase 4 complete; you can create and fetch an order.

> This phase is the payoff for the whole project. If any single operation teaches "where does logic
> go?", it's inventory reservation. Spend time on §3.

---

## Step 1 — The operations and their homes

| Operation | Rule owner | Why |
|---|---|---|
| Pricing (line prices, order total) | **Domain / domain service** | Pure calculation over domain data; must be testable without a DB. |
| Approval required over threshold | **Domain** (`Order.RequiresApproval`, checked in `Book`) | A rule about the order's own state. |
| Inventory *availability* (informational) | **Domain expresses it** | Fast feedback, testable. |
| Inventory *reservation* (the real guard) | **SQL, atomic** | Concurrency makes an in-memory check unsafe (Tension A.1). |
| Stock deduction on booking | **SQL, atomic, in the booking transaction** | Same reason; and it must be consistent with the status change. |
| Status transition Draft→Booked | **Domain** (`Order.Book`) | State-machine rule; entity owns it. |
| Optimistic concurrency (no lost updates) | **SQL** (version check) + domain surfaces the conflict | The check must be atomic at write time. |

## Step 2 — Pricing: the first *justified* domain service

Pricing needs data from more than one aggregate (materials/price lists) and doesn't belong to `Order`
alone — this is the rare case the brief means by *"when to create a service."*

```csharp
// Domain service interface (declared in Domain/Application), impl in Infrastructure if it needs data access.
public interface IPricingService
{
    Task<Money> GetUnitPrice(MaterialId material, CancellationToken ct);
    // Could grow: volume discounts, customer-specific price lists.
}
```

**When it would NOT be a service:** if pricing were "unit price × quantity" with data already on the
order, it'd be a method on `OrderLine`/`Order`, not a service. It becomes a service *only because* it
reaches outside the aggregate for price-list data. State that test out loud: *a domain service exists
when logic spans aggregates or needs data the aggregate doesn't own.* Otherwise it's a method on an
entity, and "a service" would just be the anemic-domain trap again.

> Keep `IPricingService` honest: it computes prices, it does not book orders or touch inventory. One
> responsibility. A service that starts orchestrating is a handler in disguise.

## Step 3 — Inventory reservation: the concurrency lesson (the important one)

### The naive (broken) approach

```csharp
// BROKEN under concurrency:
var inv = await inventory.Get(materialId);          // read Available
if (inv.Available >= qty)                            // check in memory
    await inventory.Reserve(materialId, qty);        // then write
```

Two requests for the last 5 units both read `Available = 5`, both pass the check, both reserve →
you've reserved 10 of 5. This is a **race condition**, and no amount of domain purity fixes it,
because the check and the write aren't atomic.

### The correct approach — atomic guard in SQL

The check and the decrement are **one statement**; the database serializes it:

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
      AND (OnHand - Reserved) >= @Qty;   -- guard is part of the write
    SELECT @@ROWCOUNT;                    -- 0 = not enough stock, 1 = reserved
END
```

The handler inspects the rowcount: `0` means "insufficient stock," which it turns into a domain-level
failure. The domain still *expresses* the availability rule (for validation and unit tests), but the
**authoritative** guard is this atomic SQL. This is Tension A.1 made real:

> **Policy** ("we reserve only what's available") is a domain concept. **Atomicity** ("no two callers
> can both win the last unit") is a database concern. Both are needed; they live in different places;
> they are not duplication — they're the same rule enforced at two levels for two reasons (fast
> feedback vs. correctness under concurrency).

### Where the reservation happens in the flow

Reservation is part of **booking** (or a distinct `ReserveInventory` step, per your workflow). It runs
inside the booking transaction so that "stock deducted/reserved" and "order booked" commit together or
not at all.

## Step 4 — BookOrder end-to-end

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

        // Domain rule: only draft, non-empty, approval if required. Throws if illegal.
        order.Book(cmd.ApprovalGranted);

        await using var tx = await uow.BeginAsync(ct);

        // Atomic reservation per line (SQL guard). If any line can't reserve, we throw -> rollback.
        foreach (var line in order.Lines)
        {
            var reserved = await inventory.TryReserve(line.MaterialId, line.Quantity, tx, ct);
            if (!reserved)
                throw new DomainException($"Insufficient stock for material {line.MaterialId.Value}.");
        }

        await orders.UpdateAsync(order, tx, ct);   // persist Booked status + optimistic version check
        await tx.CommitAsync(ct);

        // Dispatch OrderBooked domain event AFTER commit (Phase 7).
        return Unit.Value;
    }
}
```

Read the division of labor: the **domain** decides *whether* booking is legal (`order.Book`), the
**SQL** guarantees the reservation is *safe under concurrency*, and the **handler** owns the
*transaction boundary* that makes them atomic together. Three responsibilities, three homes, no
overlap. (`Unit` is a tiny "no meaningful return" type; define it in the messaging abstractions.)

## Step 5 — Optimistic concurrency on the order itself

Two managers book/edit the same order concurrently → lost update. Guard with a `rowversion`
(`ROWVERSION`/`timestamp`) column and a conditional update:

```sql
CREATE PROCEDURE usp_Order_UpdateStatus
    @OrderId BIGINT,
    @Status  INT,
    @RowVersion BINARY(8)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.[Order]
    SET Status = @Status
    WHERE OrderId = @OrderId AND RowVersion = @RowVersion;
    SELECT @@ROWCOUNT;   -- 0 => someone else changed it since we loaded it
END
```

Rowcount `0` → the repository throws a `ConcurrencyException`; the handler lets it bubble (the caller
retries with a fresh load). The domain doesn't manage `rowversion` (that's a persistence detail); the
repository loads it with the aggregate and passes it back on update. **This is the honest boundary:**
concurrency tokens are infrastructure, but the *decision* to fail on conflict is a business choice you
surface as a domain-meaningful exception.

## Step 6 — Deducting stock vs. reserving

Model the two-step reality: **reserve** on booking (holds stock so it can't be double-sold), **deduct**
`OnHand` on fulfillment/completion (Phase 7's `Completed` transition), and **release** the reservation
on cancellation. Each is an atomic SQL statement of the same shape as §3. Decide your policy explicitly:
this plan uses reserve-on-book, deduct-on-complete, release-on-cancel. Write it down so the procs stay
consistent.

## What Sonnet should produce

- `Unit` type; `BookOrderCommand` + validator + `BookOrderHandler`.
- `IInventoryService`/repository with atomic `TryReserve`, `Deduct`, `Release` backed by
  `usp_Inventory_Reserve`, `usp_Inventory_Deduct`, `usp_Inventory_Release` (all guard via rowcount).
- `IPricingService` + implementation (justified domain service); wire into `CreateOrder` and booking
  totals.
- `rowversion` column on `Order`; `usp_Order_UpdateStatus` with the version guard; `ConcurrencyException`.
- `POST /orders/{id}/book` endpoint.
- Unit tests: approval threshold, illegal booking states (domain). Integration tests for the atomic
  reservation race can wait for Phase 8 but note them here.

**NOT in this phase:** queries/read models (Phase 6), CancelOrder full flow and event dispatch
(Phase 7). Keep this phase about *write-side business rules and concurrency*.

## Pitfalls

- **Check-then-act in C#** for inventory. It looks correct in tests (no concurrency) and fails in
  production. The guard must be atomic in SQL.
- **A "manager" domain service that orchestrates booking.** That's the handler's job. Domain services
  compute/decide within the domain; they don't run transactions or call repositories to orchestrate.
- **Putting the rowversion check in the domain.** It's a persistence concern; the *conflict outcome*
  is domain-meaningful, the *mechanism* is infrastructure.
- **Forgetting to release reservations on cancel** → phantom stock shortages. Model reserve/deduct/
  release as a set.

---

## Checkpoint — you understand this phase when you can answer:

1. Why does the naive in-memory availability check fail, and what exactly makes the SQL version safe?
2. Is "reserve only if available" duplicated between domain and SQL? Explain why that's not duplication.
3. Why is `IPricingService` a legitimate domain service, and what would make it *not* one?
4. Who owns the transaction boundary in `BookOrder`, and why not the domain or the repository?
5. Where does the `rowversion` concurrency token live conceptually, and who decides to fail on conflict?
