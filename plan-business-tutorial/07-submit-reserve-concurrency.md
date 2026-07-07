# Phase 7 — Submit for Approval: Atomic Inventory Reservation & Rollback

> 📎 The concurrency lesson in slow motion, with timelines:
> [beginner Phase 5](../plan-beginner-friendly/06-phase-5-inventory-concurrency.md). This is the phase
> where the whole architecture earns its keep — the one bug clean C# *cannot* fix on its own.

---

> ⏱️ **Time for this phase:** ~1.5–2 hr. Not much *code*, but the biggest *idea* in the tutorial (atomic
> reservation under concurrency). Spend the time on the "why," not the typing.

## 1. What we're building

`POST /sales-orders/{id}/submit`. When a salesperson submits a draft order:

1. We re-check the customer isn't blocked and is still within credit (state may have changed since
   create).
2. We **atomically reserve** inventory for every line — reserve *only if available*, safe even if two
   salespeople submit for the same last unit at the same instant.
3. We move the order to **PendingApproval**.
4. **All of it in one transaction:** if *any* line can't be reserved, the whole submit rolls back — no
   partial reservations, order stays Draft.

## 2. Why it's designed this way

This phase is the concrete payoff of "policy in the domain, atomicity in SQL"
([beginner Phase 5](../plan-beginner-friendly/06-phase-5-inventory-concurrency.md)). Three different
responsibilities, three homes, no overlap:

- **Domain** decides *whether* submit is a legal transition and the order is non-empty (`order.Submit()`).
- **SQL** guarantees each reservation is *safe under concurrency* (`usp_Inventory_Reserve`, one atomic
  statement).
- **Handler** owns the *transaction boundary* that binds "reserve all lines" + "change status" into one
  all-or-nothing unit.

> ❓ **You might be wondering: "Why reserve at *submit* and not at *approve*?"** Because you don't want a
> manager approving an order you then can't fulfill. Holding the stock the moment it's submitted means
> "PendingApproval" genuinely means "we have set this aside for you." If the customer's order is later
> cancelled or rejected, we *release* the reservation ([Phase 8](08-approval-cancellation.md)). This
> reserve→(approve|release) lifecycle is a real ERP pattern.

## 3. Inventory is its own module — Sales reserves through a contract

Reserving stock is **Inventory's** job, not Sales'. So Inventory is its own module
(`Erp.Modules.Inventory`) with its own `inventory` schema, and Sales must *not* reference it or touch
`inventory.*` directly. The only channel is a **published contract** in the shared kernel — the modular
monolith's core move ([beginner Phase 5](../plan-beginner-friendly/06-phase-5-inventory-concurrency.md)):

```csharp
// Erp.Shared.Common/Contracts/IStockReservation.cs  (the ONLY way Sales touches stock)
public interface IStockReservation
{
    // Returns true if the reservation succeeded (stock was available), false otherwise.
    Task<bool> TryReserveAsync(long itemId, int qty, IDbTransaction tx, CancellationToken ct);
    Task ReleaseAsync(long itemId, int qty, IDbTransaction tx, CancellationToken ct);
}
```

The **Inventory module** implements it by calling its atomic proc from [Phase 3](03-database-schema.md);
Sales depends only on the interface, so neither module references the other. The Api wires them together.

> ⚠️ **Honest note on the `IDbTransaction tx` in this contract.** Passing a database transaction across the
> contract lets Sales and Inventory share **one** transaction — perfect and correct *inside a single-database
> monolith*, which is exactly what we have. But it's worth naming what that means for the "you could extract
> Inventory into its own service later" story you'll hear in [Phase 13](13-wrap-up-checklist.md): you
> **can't send a database transaction over a network**, so if Inventory ever moved to its own database, this
> `tx` parameter would have to disappear and the "reserve in the same transaction" step would become a
> **saga** (reserve as a separate call, and *release* to compensate if the rest fails). So extraction reshapes
> *this contract* and adds saga logic — it does **not** force you to untangle Sales from Inventory's tables,
> because those were never shared. The wall still buys you most of the payoff; just don't picture the swap as
> "same interface, zero changes." (Full version: [beginner Phase 9](../plan-beginner-friendly/10-phase-9-modularity.md).)

```csharp
// Modules/Inventory/Infrastructure/StockReservationService.cs  (Inventory owns this)
public sealed class StockReservationService : IStockReservation
{
    public async Task<bool> TryReserveAsync(long itemId, int qty, IDbTransaction tx, CancellationToken ct)
    {
        var rows = await tx.Connection!.ExecuteScalarAsync<int>(new CommandDefinition(
            "inventory.usp_Inventory_Reserve",
            new { ItemId = itemId, Qty = qty },
            transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));
        return rows == 1;   // proc returns @@ROWCOUNT: 1 = reserved, 0 = not enough stock
    }

    public async Task ReleaseAsync(long itemId, int qty, IDbTransaction tx, CancellationToken ct)
    {
        await tx.Connection!.ExecuteAsync(new CommandDefinition(
            "inventory.usp_Inventory_Release",
            new { ItemId = itemId, Qty = qty },
            transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }
}
```

The safety is entirely in that one SQL `UPDATE ... WHERE (OnHand - Reserved) >= @Qty` statement. The C#
just reads whether it reported success.

> 📎 **Why the single-statement `UPDATE` is safe when a C# read-then-write isn't** — the moment-by-moment
> two-request timeline is in
> [beginner Phase 5 §3](../plan-beginner-friendly/06-phase-5-inventory-concurrency.md). If you only
> reread one thing before this phase, reread that. It's *the* idea.

## 4. The Submit command and handler

`Modules/Sales/Features/SubmitSalesOrder/SubmitSalesOrderHandler.cs`:

```csharp
public sealed record SubmitSalesOrderCommand(long OrderId) : ICommand<Unit>;

public sealed class SubmitSalesOrderHandler(
    ISalesOrderRepository orders,
    ICustomerRepository customers,
    IStockReservation stock,        // ← the published Inventory contract, NOT Inventory's internals
    IUnitOfWork uow) : ICommandHandler<SubmitSalesOrderCommand, Unit>
{
    public async Task<Unit> Handle(SubmitSalesOrderCommand cmd, CancellationToken ct)
    {
        // 1) Load the aggregate.
        var order = await orders.GetAsync(new SalesOrderId(cmd.OrderId), ct)
            ?? throw new NotFoundException($"Sales order {cmd.OrderId} not found.");

        // 2) Re-check customer rules (state may have changed since the order was created).
        var customer = await customers.GetAsync(order.CustomerId, ct)
            ?? throw new NotFoundException("Customer not found.");
        customer.EnsureNotBlocked();
        customer.EnsureWithinCreditLimit(order.Total);

        // 3) DOMAIN: is submitting legal, and is the order non-empty? Throws if not.
        order.Submit();

        // 4) One transaction wraps EVERYTHING that follows.
        await using var tx = await uow.BeginAsync(ct);

        // 5) Atomically reserve each line. If ANY fails -> throw -> whole tx rolls back.
        foreach (var line in order.Lines)
        {
            var reserved = await stock.TryReserveAsync(line.ItemId.Value, line.Quantity.Value, tx.Transaction, ct);
            if (!reserved)
                throw new DomainException(
                    $"Insufficient stock for item {line.ItemId.Value}. Submission cancelled.");
        }

        // 6) Persist the new status (version-checked — Phase 8 explains the RowVersion guard).
        await orders.UpdateStatusAsync(order, tx.Transaction, ct);

        // 7) Commit: reservations + status change become permanent together.
        await tx.CommitAsync(ct);

        return Unit.Value;
    }
}
```

### Why the transaction wraps *all* the reservations

Imagine an order with 3 lines. Line 1 and line 2 reserve fine, but line 3 is out of stock. Without the
transaction, you'd have reserved stock for lines 1 and 2 but *failed the submit* — phantom reservations
that no order actually holds. Inside the transaction, throwing on line 3 causes `DisposeAsync` to roll
back lines 1 and 2's reservations too. **Everything, or nothing.**

> 🔑 This is the "transaction rollback on failure" requirement made concrete. The rollback is *automatic*
> because we `throw` before `CommitAsync`, and the `await using` disposes the uncommitted transaction —
> exactly the mechanism from [Phase 6 §7](06-create-order.md). You don't write a `Rollback` call; you
> write correct `throw`s and let the transaction boundary do its job.

## 5. Walk the concurrency scenario (the payoff moment)

Item 3 (*Restricted Kit*) has `OnHand = 8, Reserved = 0`. Two managers each submit an order for **5**
units at the same instant (managers, because it's the Controlled category — see the role rule).

```
 time   Submit A (5 units)                       Submit B (5 units)
 ────    ─────────────────────────────────────    ─────────────────────────────────────
  t1     BEGIN TRAN
  t2     usp_Inventory_Reserve(item3, 5)
         UPDATE ... WHERE (8-0) >= 5  → matches
         Reserved = 5, rowcount = 1  → reserved ✓
  t3                                              BEGIN TRAN
  t4                                              usp_Inventory_Reserve(item3, 5)
                                                  UPDATE ... WHERE (8-5) >= 5 → 3 >= 5 FALSE
                                                  rowcount = 0 → NOT reserved ✗
  t5     UpdateStatus → PendingApproval
  t6     COMMIT  (Reserved now 5, durable)
  t7                                              handler sees rows==0 → throws DomainException
  t8                                              tx disposes uncommitted → ROLLBACK
                                                  → B's order stays Draft, gets a 422 "Insufficient stock"
```

**Exactly one manager wins the stock. No overselling. No luck.** And because the database enforces
`Reserved <= OnHand` as a `CHECK` too ([Phase 3](03-database-schema.md)), even a hypothetical bug
couldn't push it negative. This is the bug that unit tests can't catch and integration tests must
([Phase 12](12-testing.md)).

> ❓ **You might be wondering: "Does the database really stop B from reading stale stock like the C#
> version did?"** Yes. Each `UPDATE` takes a brief lock on the row. B's `UPDATE` can't evaluate its
> `WHERE` until A's `UPDATE` (and, for the committed value, A's commit) has released the row. So B
> evaluates against `Reserved = 5`, sees only 3 available, and correctly fails. The single-statement
> update is what makes "check" and "change" inseparable.

## 6. The endpoint (Carter module in the feature folder)

`Modules/Sales/Features/SubmitSalesOrder/SubmitSalesOrderEndpoint.cs`:

```csharp
public sealed class SubmitSalesOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/sales-orders/{id:long}/submit",
            async (long id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new SubmitSalesOrderCommand(id), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole("SalesRep", "SalesManager", "Admin"));
}
```

## 7. Why each component exists

| Component | Why |
|---|---|
| `SubmitSalesOrderCommand` | Carries which order to submit. |
| `SubmitSalesOrderHandler` | Orchestrates re-checks + reservation + status change inside one transaction. |
| `IStockReservation.TryReserveAsync` | The cross-module contract to Inventory's atomic guard; success/failure. |
| `usp_Inventory_Reserve` | The *actual* concurrency-safe guard (the whole reason this works). |
| `IUnitOfWork` transaction | Binds all reservations + the status change into all-or-nothing. |
| `order.Submit()` | Domain's legality + non-empty check; no DB knowledge. |

## 8. Common mistakes & best practices

- ❌ **Check-then-act in C#**: `var inv = Get(); if (inv.Available >= qty) Reserve();`. Looks correct,
  passes single-threaded tests, oversells in production. ✅ One atomic SQL statement; inspect rowcount.
- ❌ **Reserving each line in its own transaction.** ✅ One transaction for the whole submit, so a later
  failure rolls back earlier reservations.
- ❌ **Reserving inside the domain method `Submit()`.** ✅ Domain stays pure; the handler + SQL reserve.
- ❌ **Ignoring the reservation rowcount** and assuming success. ✅ `rows == 1` or it didn't happen.
- ❌ **Not re-checking blocked/credit at submit** because you checked at create. ✅ State drifts;
  re-validate at the moment it matters. *(Caveat: like at create, this credit check still only weighs
  **this** order against the committed balance — not the customer's other in-flight orders. See the ⚠️ note
  in [Phase 5 §7](05-domain-model.md) for why, and the fix, if you want true pipeline-wide credit.)*
- ✅ **Best practice: keep the transaction short.** Do read-only validation *before* `BeginAsync`; only
  the writes (reserve + status) live inside the transaction, minimizing how long rows are locked.

---

## ✅ Check Yourself

1. Why is one atomic SQL `UPDATE` safe where a C# read-then-write race-conditions?
2. Why must all the line reservations and the status change share one transaction?
3. Where does the rollback "happen" in the code — what triggers it?
4. Why reserve at submit rather than at approval?

> 👉 Next: [Phase 8 — Approval & cancellation (roles, state machine, release, optimistic concurrency)](08-approval-cancellation.md).
