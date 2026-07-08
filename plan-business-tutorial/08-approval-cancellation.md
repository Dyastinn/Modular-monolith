# Phase 8 — Approval & Cancellation (Roles, State Machine, Release, Concurrency)

> Self-contained: the state machine, optimistic concurrency, and event dispatch are all explained here.
> Money is in **Philippine Pesos (₱)**.
>
> 📎 *Optional* deeper reading: the state machine and domain-event ideas
> ([beginner Phase 7](../plan-beginner-friendly/08-phase-7-remaining-slices.md)) and the optimistic
> concurrency guard ([beginner Phase 5](../plan-beginner-friendly/06-phase-5-inventory-concurrency.md)).

---

> ⏱️ **Time for this phase:** ~2 hr. Two commands, the role gate, optimistic concurrency, matched
> release/reversal pairs, and after-commit event dispatch — several distinct ideas in one sitting.

## 1. What we're building

Two commands that complete the order lifecycle:

- `POST /sales-orders/{id}/approve` — **SalesManager only** (role string `"SalesManager"`). Moves PendingApproval → Approved, and adds the
  order total to the customer's outstanding balance (they now owe it).
- `POST /sales-orders/{id}/cancel` — Moves an order to Cancelled and **releases** any inventory it had
  reserved, so the stock returns to available.

Plus: **optimistic concurrency** so two managers acting on the same order can't clobber each other, and
**domain-event dispatch after commit** for side effects (audit logging).

## 2. Why it's designed this way

- **Approval is role-gated at two levels.** Coarse: the endpoint requires the `SalesManager` role. This
  is the "endpoint-level authorization" from [Phase 4 §9](04b-jwt-login.md) — a Manager-only action, full
  stop, so it belongs on the endpoint.
- **Cancellation releases reservations.** If we didn't, cancelled orders would hold stock forever —
  "phantom shortages." The reserve→release lifecycle from [Phase 7](07-submit-reserve-concurrency.md)
  must be closed.
- **Optimistic concurrency** protects the *order row* itself (two managers, one order), distinct from the
  inventory concurrency (two orders, one stock item). Both matter; they're different problems.

## 3. The Approve command and handler

`Modules/Sales/Features/ApproveSalesOrder/ApproveSalesOrderHandler.cs`:

```csharp
public sealed record ApproveSalesOrderCommand(long OrderId) : ICommand<Unit>;

public sealed class ApproveSalesOrderHandler(
    ISalesOrderRepository orders,
    ICustomerRepository customers,
    IDbConnectionFactory factory,
    ICurrentUser currentUser,
    IDomainEventDispatcher events) : ICommandHandler<ApproveSalesOrderCommand, Unit>   // command marker (Phase 2)
{
    public async Task<Unit> Handle(ApproveSalesOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetAsync(new SalesOrderId(cmd.OrderId), ct)
            ?? throw new NotFoundException($"Sales order {cmd.OrderId} not found.");

        // DOMAIN: only a PendingApproval order can be approved (state machine). Throws otherwise.
        order.Approve(new UserId(currentUser.UserId));

        using var conn = factory.Create();
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        try
        {
            // Version-checked status update: fails if someone changed the order since we loaded it.
            await orders.UpdateStatusAsync(order, tx, ct);

            // The customer now owes this money — add it to their outstanding balance, same transaction.
            await customers.AdjustBalanceAsync(order.CustomerId, +order.Total.Amount, tx, ct);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        // AFTER commit: dispatch domain events (audit log, etc.). Best-effort side effects.
        await events.DispatchAsync(order, ct);
        return Unit.Value;
    }
}
```

> ❓ **You might be wondering: "Why adjust the customer balance here and not at create or submit?"**
> Because *approval* is the moment the company commits to the sale — that's when the customer genuinely
> owes it. At create it's a draft (might be abandoned); at submit it's pending (might be rejected). Tying
> the balance change to approval, in the *same transaction* as the status change, keeps "Approved" and
> "balance increased" perfectly consistent — they commit together or not at all.

## 4. Optimistic concurrency: the version-checked update

Recall the order table has a `RowVersion` stamp ([Phase 3](03-database-schema.md)). The repository loads
it with the order and passes it back on update:

```csharp
public async Task UpdateStatusAsync(SalesOrder order, IDbTransaction tx, CancellationToken ct)
{
    var rows = await tx.Connection!.ExecuteScalarAsync<int>(new CommandDefinition(
        "sales.usp_SalesOrder_UpdateStatus",
        new
        {
            Id = order.Id.Value,
            Status = (int)order.Status,
            ApprovedByUserId = order.Status == SalesOrderStatus.Approved ? (long?)order.ApprovedBy?.Value : null,
            RowVersion = order.RowVersion   // the stamp we read when loading
        },
        transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));

    if (rows == 0)
        throw new ConcurrencyException(
            "This order was changed by someone else. Reload and try again.");
}
```

The proc's `WHERE Id = @Id AND RowVersion = @RowVersion` matches **only if** nobody else changed the row
since we loaded it (any change bumps `RowVersion`). Rowcount `0` → someone beat us → we throw
`ConcurrencyException`, the transaction rolls back, and the caller is told to reload.

> 🔑 **Two managers, one order:** both load order #50 (same `RowVersion`). Manager A approves → row
> updates, `RowVersion` changes, commit. Manager B clicks reject a second later → their `UPDATE`'s
> `WHERE RowVersion = <old>` matches **nothing** (the stamp changed) → rowcount 0 → `ConcurrencyException`.
> B is safely told the order already changed, instead of silently overwriting A's approval. This is the
> "lost update" prevented — [beginner Phase 5 §5](../plan-beginner-friendly/06-phase-5-inventory-concurrency.md).

> ❓ **You might be wondering: "Isn't the transaction enough? Why also need `RowVersion`?"** No — a
> transaction guarantees *your* writes are atomic, but it doesn't stop you from *overwriting* a change
> someone committed *before* you started. `RowVersion` catches "the world moved under me since I read
> it." Transaction = my writes are all-or-nothing; RowVersion = I'm not clobbering someone else's
> committed change. You need both.

## 5. The Cancel command and handler (release reservations)

`Modules/Sales/Features/CancelSalesOrder/CancelSalesOrderHandler.cs`:

```csharp
public sealed record CancelSalesOrderCommand(long OrderId) : ICommand<Unit>;

public sealed class CancelSalesOrderHandler(
    ISalesOrderRepository orders,
    ICustomerRepository customers,  // needed to reverse the balance if the order was already Approved
    IStockReservation stock,        // the Inventory contract — release goes back across the wall
    IDbConnectionFactory factory,
    IDomainEventDispatcher events) : ICommandHandler<CancelSalesOrderCommand, Unit>   // command marker (Phase 2)
{
    public async Task<Unit> Handle(CancelSalesOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetAsync(new SalesOrderId(cmd.OrderId), ct)
            ?? throw new NotFoundException($"Sales order {cmd.OrderId} not found.");

        // Capture what we must undo BEFORE we change the status.
        var hadReservation = order.Status is SalesOrderStatus.PendingApproval or SalesOrderStatus.Approved;
        // An APPROVED order already added its total to the customer's balance (see Approve above).
        // Cancelling it must give that credit back, or the customer "owes" a cancelled order forever.
        var wasApproved = order.Status == SalesOrderStatus.Approved;
        var total = order.Total;

        // DOMAIN: is cancelling legal from the current status? Throws if not (e.g. already Cancelled).
        order.Cancel();

        using var conn = factory.Create();
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        try
        {
            // If the order was holding stock (submitted/approved), give it back — same transaction.
            if (hadReservation)
                foreach (var line in order.Lines)
                    await stock.ReleaseAsync(line.ItemId.Value, line.Quantity.Value, tx, ct);

            // If we had already charged the customer at approval, reverse it now (a matched debit/credit pair).
            if (wasApproved)
                await customers.AdjustBalanceAsync(order.CustomerId, -total.Amount, tx, ct);

            await orders.UpdateStatusAsync(order, tx, ct);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        await events.DispatchAsync(order, ct);
        return Unit.Value;
    }
}
```

> ❓ **You might be wondering: "Why the `hadReservation` check — why not always release?"** A *Draft*
> order never reserved anything (reservation happens at submit), so releasing would wrongly *add* stock
> that was never removed. We only release if the order actually held a reservation
> (PendingApproval/Approved). Getting this wrong is a classic bug: cancelling a draft "creates" phantom
> stock. Model reserve/release as a matched pair, always.

> 🔑 **The balance is a matched pair too.** We add to `OutstandingBalance` at *approval* and subtract it
> at *cancellation of an approved order* — in the **same transaction** as the status change, so status and
> balance can never disagree. Forgetting the reversal is exactly as broken as forgetting to release stock:
> the customer would keep "owing" money for an order that no longer exists, silently eating into their
> credit limit for future orders. Whenever a write *adds* something (stock reserved, money owed), find the
> command that *undoes* it and make sure the opposite write is there. (Rejection from `PendingApproval`
> needs no reversal — we hadn't charged the balance yet; the charge only happens at approval.)

## 6. Domain event dispatch after commit

The domain events raised in [Phase 5](05-domain-model.md) (`SalesOrderApproved`, etc.) get dispatched
*after* the transaction commits. Minimal dispatcher (Infrastructure):

First, the two contracts — a handler for one event type, and the dispatcher that runs them. These belong in
`Erp.Shared.Messaging` (every module's events reuse them), alongside the other messaging contracts:

> ✅ **No new reference needed here.** These contracts mention `IDomainEvent` and `AggregateRoot<TId>`, which
> live in `Erp.Shared.BuildingBlocks` — and you already gave `Erp.Shared.Messaging` a reference to
> `BuildingBlocks` back in [Phase 1](01-solution-setup.md) (that's the edge we set up precisely so the
> mediator could carry base domain-event types). So they resolve as-is. Safe edge, too: `BuildingBlocks`
> depends on nothing, so no module wall is crossed.

```csharp
// A reaction to a specific domain event. Zero-to-many handlers may exist per event.
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task Handle(TEvent domainEvent, CancellationToken ct);
}

// Note the GENERIC TId — the dispatcher works for ANY aggregate (SalesOrder, Inventory's Stock, …),
// so this shared type isn't coupled to a Sales-specific id.
public interface IDomainEventDispatcher
{
    Task DispatchAsync<TId>(AggregateRoot<TId> agg, CancellationToken ct) where TId : struct;
}
```

```csharp
public sealed class DomainEventDispatcher(IServiceProvider sp) : IDomainEventDispatcher
{
    public async Task DispatchAsync<TId>(AggregateRoot<TId> agg, CancellationToken ct) where TId : struct
    {
        foreach (var e in agg.DomainEvents)
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(e.GetType());
            foreach (var handler in sp.GetServices(handlerType))
                await (Task)handlerType.GetMethod("Handle")!.Invoke(handler, new object[] { e, ct })!;
        }
        agg.ClearEvents();
    }
}
```

> 🔑 **Why generic `<TId>` and not `AggregateRoot<SalesOrderId>`?** This dispatcher lives in the *shared*
> kernel, so it must not know about a Sales-specific type like `SalesOrderId`. Making `DispatchAsync<TId>`
> generic keeps it usable by every module's aggregates while staying inside the module walls — a small change
> with a real boundary payoff.

An example handler that logs an audit entry:

```csharp
public sealed class SalesOrderApprovedAuditHandler(ILogger<SalesOrderApprovedAuditHandler> log)
    : IDomainEventHandler<SalesOrderApproved>
{
    public Task Handle(SalesOrderApproved e, CancellationToken ct)
    {
        log.LogInformation("Order {Id} approved by user {Approver} for ₱{Total:N2}",
            e.Id.Value, e.Approver.Value, e.Total.Amount);
        return Task.CompletedTask;
    }
}
```

> ℹ️ **Register these like handlers.** `IDomainEventHandler<>` implementations are found the same way as your
> request handlers — extend `AddSalesModule`'s assembly scan to also register every closed
> `IDomainEventHandler<>` in the module (the same reflection loop from
> [Phase 2](02-mediator-own-vs-free.md), swapping `IRequestHandler<,>` for `IDomainEventHandler<>`). A handler
> that isn't registered simply never runs — and since dispatch is best-effort after commit, you'd get *silent*
> no-ops, so it's worth a quick check that yours are picked up.

> 📎 **Why dispatch *after* commit** (and the consequence if a handler fails) —
> [beginner Phase 7 §3](../plan-beginner-friendly/08-phase-7-remaining-slices.md). Short version: only
> react to things that *actually saved*; if the audit log fails, the order is still correctly approved
> (a failed log shouldn't un-approve an order). Reliable cross-system reactions would need a queue/outbox
> — deliberately out of scope here (that's the [beginner Phase 9](../plan-beginner-friendly/10-phase-9-modularity.md)
> "next step").

> 🔗 **Same mechanism, two names.** The [beginner course](../plan-beginner-friendly/08-phase-7-remaining-slices.md)
> introduces this dispatch under the *notification* vocabulary. It's one-to-one with what we build here:
> `INotification` ↔ our `IDomainEvent`, `INotificationHandler<T>` ↔ our **`IDomainEventHandler<T>`**, and
> `IPublisher` ↔ our **`IDomainEventDispatcher`**. Same pattern (publish an event to zero-or-more handlers
> after commit) — just pick one naming set and stay consistent in your own build.

## 7. The endpoints (each a Carter module in its feature folder; note the role gate on approve)

```csharp
// Modules/Sales/Features/ApproveSalesOrder/ApproveSalesOrderEndpoint.cs
public sealed class ApproveSalesOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/sales-orders/{id:long}/approve",
            async (long id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new ApproveSalesOrderCommand(id), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole("SalesManager", "Admin"));   // ← MANAGER ONLY
}

// Modules/Sales/Features/CancelSalesOrder/CancelSalesOrderEndpoint.cs
public sealed class CancelSalesOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/sales-orders/{id:long}/cancel",
            async (long id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new CancelSalesOrderCommand(id), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole("SalesRep", "SalesManager", "Admin"));
}
```

The approve endpoint's `RequireRole("SalesManager", "Admin")` means a SalesRep's token is rejected with
**403 Forbidden** *before the handler even runs*. That's the coarse, endpoint-level authorization — the
right home for a blanket "managers only" rule.

## 8. How the approval request flows

Manager logged in as `manager` approves order #12 (PendingApproval, total ₱12,000, customer Acme):

```
1. Auth: token valid, role SalesManager.
2. UseAuthorization: endpoint requires SalesManager/Admin → passes. (A SalesRep would 403 here.)
3. Handler loads order #12 with its RowVersion.
4. order.Approve(managerId) → state machine allows PendingApproval→Approved ✓; raises SalesOrderApproved.
5. BEGIN TRAN.
6. UpdateStatus with RowVersion → nobody else touched it → rowcount 1 ✓.
7. AdjustBalance(Acme, +₱12,000) → Acme now owes ₱22,000 of their ₱50,000 limit.
8. COMMIT — status + balance change durable together.
9. After commit: dispatch SalesOrderApproved → audit log writes "Order 12 approved...".
10. Endpoint returns 204 No Content.
```

If a SalesRep had tried, they'd never reach step 3 — the 403 stops them at step 2.

## 9. Why each component exists

| Component | Why |
|---|---|
| `ApproveSalesOrderHandler` | Orchestrates status change + balance update in one transaction, then events. |
| `CancelSalesOrderHandler` | Orchestrates status change + reservation release in one transaction. |
| `usp_SalesOrder_UpdateStatus` (version-checked) | Prevents lost updates between concurrent managers. |
| `ConcurrencyException` | Turns "row changed under me" into a meaningful, retryable error. |
| `IDomainEventDispatcher` + handlers | Runs side effects after commit, decoupled from the domain. |
| Endpoint `RequireRole` | Coarse manager-only gate, enforced before the handler. |

## 10. Common mistakes & best practices

- ❌ **Approving with no role check** ("we'll trust the UI"). ✅ Enforce on the server endpoint; never
  trust the client.
- ❌ **Releasing stock on cancelling a Draft.** ✅ Only release if the order actually reserved
  (PendingApproval/Approved).
- ❌ **Cancelling an *approved* order without reversing the customer balance.** ✅ If approval charged the
  balance, cancellation must credit it back — same transaction, a matched pair.
- ❌ **Relying on the transaction alone for concurrent edits.** ✅ Add the `RowVersion` check for lost
  updates — transactions and optimistic concurrency solve *different* problems.
- ❌ **Dispatching events before commit.** ✅ After commit only, so you don't react to a rolled-back
  change.
- ❌ **Adjusting the customer balance at create/submit.** ✅ At approval — the moment the debt is real —
  and in the same transaction as the status change.
- ✅ **Best practice: surface `ConcurrencyException` as a 409 Conflict** (Phase 11) so clients know to
  reload and retry.

## 11. Checkpoint — approve (manager-only) and cancel (releases) ✅

Take a `PendingApproval` order from Phase 7. First prove the role gate — with a **`rep`** token:

```bash
curl -X POST http://localhost:5xxx/sales-orders/{id}/approve -H "Authorization: Bearer $REP_TOKEN"   # → 403 Forbidden
```

Now approve it as **`manager`**, and confirm the balance moved:

```bash
curl -X POST http://localhost:5xxx/sales-orders/{id}/approve -H "Authorization: Bearer $MANAGER_TOKEN"  # → 204
```
```sql
SELECT Status FROM sales.SalesOrder WHERE Id = {id};                  -- 2 = Approved
SELECT OutstandingBalance FROM sales.Customer WHERE Id = {customerId};-- rose by the order total
```

Finally, cancel it and watch the reversals — stock released **and** the balance credited back:

```bash
curl -X POST http://localhost:5xxx/sales-orders/{id}/cancel -H "Authorization: Bearer $MANAGER_TOKEN"   # → 204
```

`Reserved` on the ordered items drops, and `OutstandingBalance` returns to where it was. If a rep gets `403`,
a manager can approve, and cancelling undoes both the stock hold and the balance, **Phase 8 is done and
verifiable** — the full order lifecycle now works end to end.

---

## ✅ Check Yourself

1. Why does approval need *both* a transaction and a `RowVersion` check — what does each prevent?
2. Why is the customer balance adjusted at approval rather than at create or submit?
3. Why is releasing stock guarded by `hadReservation`?
4. Which authorization here is endpoint-level and why is that the right place for it?

> 👉 Next: [Phase 9 — Updating a draft order's lines](09-update-order.md).
