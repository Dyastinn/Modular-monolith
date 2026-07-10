# Phase 7 — Submit for Approval: Atomic Inventory Reservation & Rollback

> Self-contained, including creating the second module. Money is in **Philippine Pesos (₱)**.
>
> This is the phase where the whole architecture earns its keep — it's the one bug that clean C# *cannot*
> fix on its own. Take it slowly.

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

This phase is the concrete payoff of "policy in the domain, atomicity in SQL." Three different
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
`inventory.*` directly. The only channel is a **published contract** in the shared kernel. This is the
modular monolith's core move: the *absence* of a project reference is what makes the wall real.

> 📦 **Create the second module now (Phase 1 deliberately deferred it — here's where we need it):**
> ```bash
> dotnet new classlib -n Erp.Modules.Inventory -o src/Modules/Inventory
> dotnet sln add src/Modules/Inventory
> # Inventory needs only the shared kernel — NOT Sales (the wall stays up):
> dotnet add src/Modules/Inventory/Erp.Modules.Inventory.csproj reference src/Shared/Erp.Shared.Common/Erp.Shared.Common.csproj src/Shared/Erp.Shared.Infrastructure/Erp.Shared.Infrastructure.csproj
> # The host references the new module so it can register it:
> dotnet add src/Erp.Api/Erp.Api.csproj reference src/Modules/Inventory/Erp.Modules.Inventory.csproj
> # Inventory runs Dapper against SQL Server, same as Sales:
> dotnet add src/Modules/Inventory package Dapper
> dotnet add src/Modules/Inventory package Microsoft.Extensions.DependencyInjection.Abstractions
> ```
> Give it a `DependencyInjection.cs` with `AddInventoryModule()` (registering `IStockReservation` →
> `StockReservationService`), and add `builder.Services.AddInventoryModule(builder.Configuration);` next to
> `AddSalesModule` in `Program.cs`. Notice Sales still has **no** reference to Inventory — they meet only at
> the `IStockReservation` contract below.
>
> **The module also brings its own SQL.** Data ownership follows the module, so
> `src/Modules/Inventory/Infrastructure/Sql/` gets the three scripts you wrote in
> [Phase 3](03-database-schema.md) for the `inventory` schema — `001_schema.sql` (the `Inventory` table and its
> `CHECK`s), `002_procedures.sql` (`usp_Inventory_Reserve` and `usp_Inventory_Release`), and `003_seed.sql`
> (the stock levels). They run *after* the Sales scripts, since the seed refers to item ids Sales created.

```csharp
// src/Shared/Erp.Shared.Common/Contracts/IStockReservation.cs  (the ONLY way Sales touches stock)
using System.Data;   // IDbTransaction

namespace Erp.Shared.Common;

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
> "same interface, zero changes." [Phase 13](13-wrap-up-checklist.md) revisits what extraction really costs.

```csharp
// src/Modules/Inventory/Infrastructure/StockReservationService.cs  (Inventory owns this)
using System.Data;         // CommandType, IDbTransaction
using Dapper;              // ExecuteAsync, ExecuteScalarAsync, CommandDefinition
using Erp.Shared.Common;   // IStockReservation

namespace Erp.Modules.Inventory.Infrastructure;

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

> 🔑 **Why the single-statement `UPDATE` is safe when a C# read-then-write isn't.** The two-request timeline
> in §5 below walks through it moment by moment. If you only reread one thing in this tutorial, reread that.
> It's *the* idea.

## 4. The Submit command and handler

`Modules/Sales/Features/SubmitSalesOrder/SubmitSalesOrderHandler.cs`:

```csharp
// src/Modules/Sales/Features/SubmitSalesOrder/SubmitSalesOrderHandler.cs
using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;   // DomainException
using Erp.Shared.Common;           // NotFoundException, IStockReservation
using Erp.Shared.Common.Data;      // IDbConnectionFactory
using Erp.Shared.Messaging;

namespace Erp.Modules.Sales.Features;

public sealed record SubmitSalesOrderCommand(long OrderId) : ICommand<Unit>;

public sealed class SubmitSalesOrderHandler(
    ISalesOrderRepository orders,
    ICustomerRepository customers,
    IStockReservation stock,        // ← the published Inventory contract, NOT Inventory's internals
    IDbConnectionFactory factory) : ICommandHandler<SubmitSalesOrderCommand, Unit>
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
        using var conn = factory.Create();
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        try
        {
            // 5) Atomically reserve each line. If ANY fails -> throw -> whole tx rolls back.
            foreach (var line in order.Lines)
            {
                var reserved = await stock.TryReserveAsync(line.ItemId.Value, line.Quantity.Value, tx, ct);
                if (!reserved)
                    throw new DomainException(
                        $"Insufficient stock for item {line.ItemId.Value}. Submission cancelled.");
            }

            // 6) Persist the new status (version-checked — Phase 8 explains the RowVersion guard).
            await orders.UpdateStatusAsync(order, tx, ct);

            // 7) Commit: reservations + status change become permanent together.
            tx.Commit();
        }
        catch
        {
            tx.Rollback();   // any reservation failure or error → every reservation + the status change roll back
            throw;
        }

        return Unit.Value;
    }
}
```

### Why the transaction wraps *all* the reservations

Imagine an order with 3 lines. Line 1 and line 2 reserve fine, but line 3 is out of stock. Without the
transaction, you'd have reserved stock for lines 1 and 2 but *failed the submit* — phantom reservations
that no order actually holds. Inside the transaction, throwing on line 3 jumps to the `catch`, which rolls
back lines 1 and 2's reservations too. **Everything, or nothing.**

> 🔑 This is the "transaction rollback on failure" requirement made concrete. Throwing before `tx.Commit()`
> sends control to the `catch`, which calls `tx.Rollback()` on the uncommitted transaction — exactly the
> mechanism from [Phase 6 §7](06-create-order.md). Write correct `throw`s and let the `catch` (backed by the
> `using`) undo the partial work.

## 5. Walk the concurrency scenario (the payoff moment)

### First, the version you would have written

Before the timeline, write the obvious code — the code almost everyone writes, the code that passes every
test you would think to write:

```csharp
// DON'T DO THIS. It is wrong, and it will not look wrong.
var inventory = await stock.GetAsync(line.ItemId.Value, ct);      // read:  OnHand 8, Reserved 0
if (inventory.OnHand - inventory.Reserved < line.Quantity.Value)  // check: 8 available, need 5 → fine
    throw new DomainException("Insufficient stock.");
await stock.ReserveAsync(line.ItemId.Value, line.Quantity.Value, ct);   // act:  Reserved = 5
```

Read it again. It checks the stock, and only reserves if there's enough. What could go wrong?

**The gap between the check and the act.** Those are three separate trips to the database, and between the
`if` and the `ReserveAsync`, *anything can happen* — including another salesperson running the exact same
three lines. Both read `8 available`. Both conclude `5 ≤ 8`. Both reserve 5. You have now promised 10 units
of an item you have 8 of, and nothing in your C# is wrong, and no unit test will ever catch it, because a
unit test runs one thread at a time.

This is a **race condition**, and it is not a bug you fix by being careful. You fix it by removing the gap —
by making the *check* and the *act* the same, indivisible operation. Only the database can do that, which is
why `usp_Inventory_Reserve` puts the check in the `WHERE` clause of the very `UPDATE` that does the writing:

```sql
UPDATE inventory.Inventory
SET Reserved = Reserved + @Qty
WHERE ItemId = @ItemId AND (OnHand - Reserved) >= @Qty;   -- check and act, one statement, no gap
SELECT @@ROWCOUNT;                                        -- 1 = you got it, 0 = someone beat you
```

Now watch it work.

### The timeline

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
  t8                                              catch → tx.Rollback()
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
// src/Modules/Sales/Features/SubmitSalesOrder/SubmitSalesOrderEndpoint.cs
using Carter;
using Erp.Shared.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

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
| The transaction (`BeginTransaction`) | Binds all reservations + the status change into all-or-nothing. |
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
- ✅ **Best practice: keep the transaction short.** Do read-only validation *before* `BeginTransaction()`; only
  the writes (reserve + status) live inside the transaction, minimizing how long rows are locked.

## 9. Checkpoint — submit reserves stock, over-submit rolls back ✅

Create a draft order (Phase 6), note its id, then submit it (both headers, as always since
[Phase 4c](04c-jwt-refresh-apikey.md)):

```bash
ID=1   # the id returned by POST /sales-orders
curl -X POST "$BASE/sales-orders/$ID/submit" \
     -H "X-Api-Key: $KEY" -H "Authorization: Bearer $TOKEN"    # → 204 No Content
```

Verify the reservation actually happened — the order is now `PendingApproval` and stock moved from `OnHand`
into `Reserved`:

```sql
SELECT Status FROM sales.SalesOrder WHERE Id = {id};          -- 1 = PendingApproval
SELECT OnHand, Reserved FROM inventory.Inventory WHERE ItemId = 1;   -- Reserved went up by the ordered qty
```

Now prove the guard: order more of the *Restricted Kit* (item 3, only 8 on hand) than is available and
submit — you get a `422` "Insufficient stock", and `SELECT Reserved FROM inventory.Inventory WHERE ItemId = 3`
is **unchanged** (the whole submit rolled back). If a good submit reserves and a greedy one leaves stock
untouched, **Phase 7 is done and verifiable** — you've watched the atomic guard do the one job clean C#
couldn't.

---

## ✅ Check Yourself

1. Why is one atomic SQL `UPDATE` safe where a C# read-then-write race-conditions?
2. Why must all the line reservations and the status change share one transaction?
3. Where does the rollback "happen" in the code — what triggers it?
4. Why reserve at submit rather than at approval?

> 👉 Next: [Phase 8 — Approval & cancellation (roles, state machine, release, optimistic concurrency)](08-approval-cancellation.md).
