# Phase 7 — Finishing the Write Side: Cancel, Update, Complete, and "Something Happened" Events

> 📄 Expert version: [`../plan-Opus/08-phase-7-remaining-slices.md`](../plan-Opus/08-phase-7-remaining-slices.md)

**In one sentence:** We complete the remaining features (`CancelSalesOrder`, `UpdateSalesOrder`,
`CompleteSalesOrder`), enforce the full status state machine, and learn how "something happened" events get
delivered without an ORM to do it for us — including how they can cross the module wall.

**What you'll build:** The `CancelSalesOrder`, `UpdateSalesOrder`, and `CompleteSalesOrder` features (two
files each); the enforced `SalesOrderStatus` transition table; and a domain-event dispatch mechanism that
runs *after* commit.

**Before you start:** Phases 4–6. You can create, submit, and read orders.

---

## Step 1 — CancelSalesOrder and the state machine

Cancelling isn't always allowed — a `Completed` order can't be cancelled, and a `Cancelled` one can't be
cancelled *again*. That's the state machine from Phase 3, now enforced for real. And because submitting
reserved stock (through Inventory's contract), cancelling must *release* it — through the same contract:

```csharp
// Modules/Sales/Features/CancelSalesOrder/CancelSalesOrderHandler.cs
public sealed record CancelSalesOrderCommand(long SalesOrderId) : ICommand<Unit>;

public sealed class CancelSalesOrderHandler(
    ISalesOrderRepository orders, IStockReservation stock, IUnitOfWork uow)
    : ICommandHandler<CancelSalesOrderCommand, Unit>
{
    public async Task<Unit> Handle(CancelSalesOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetAsync(new SalesOrderId(cmd.SalesOrderId), ct)
            ?? throw new NotFoundException($"Sales order {cmd.SalesOrderId} not found.");

        order.Cancel();   // DOMAIN decides if the transition is legal (throws if not)

        await using var tx = await uow.BeginAsync(ct);
        // If it was submitted, give back the stock we reserved in Phase 5 — through the Inventory contract.
        foreach (var line in order.Lines)
            await stock.ReleaseAsync(line.ProductId.Value, line.Quantity.Value, tx.Inner, ct);
        await orders.UpdateAsync(order, tx.Inner, ct);   // version-checked status update, on the same transaction
        await tx.CommitAsync(ct);

        return Unit.Value;   // SalesOrderCancelled event dispatched after commit (Step 3)
    }
}
```

The handler decides *nothing* about whether cancellation is legal — `order.Cancel()` does, via
`CanTransitionTo`. The handler only orchestrates the side effects (release reservations through the contract)
and the transaction.

> 💡 By now the handler should look *repetitive* — same shape as `SubmitSalesOrder`. **That repetition is the
> win.** Every command reads the same way, so any developer can predict what a new one looks like.

**Enforce the transition table** so illegal moves are impossible (in `Sales/Domain/`):

```csharp
private static readonly Dictionary<SalesOrderStatus, SalesOrderStatus[]> _allowed = new()
{
    [SalesOrderStatus.Draft]     = new[] { SalesOrderStatus.Submitted, SalesOrderStatus.Cancelled },
    [SalesOrderStatus.Submitted] = new[] { SalesOrderStatus.Completed, SalesOrderStatus.Cancelled },
    [SalesOrderStatus.Cancelled] = Array.Empty<SalesOrderStatus>(),
    [SalesOrderStatus.Completed] = Array.Empty<SalesOrderStatus>(),
};
public static bool CanTransitionTo(this SalesOrderStatus from, SalesOrderStatus to) =>
    _allowed[from].Contains(to);
```

The `Dictionary` is a lookup table — "from this status, here are the statuses you may move to." `Cancelled`
and `Completed` have `Array.Empty` — final, dead-end states. So `order.Cancel()` on a `Completed` order
returns `false` and throws. The illegal move is *impossible*, not just discouraged.

> ❓ **"Why a lookup table instead of a bunch of `if`s?"** Two wins: (1) all the rules sit in *one readable
> place* — you can hand the whole state machine to a non-programmer; (2) adding a status means adding one
> line, not hunting scattered `if`s. When rules form a clear grid, a table beats branching.

## Step 2 — UpdateSalesOrder: a deliberately *restrained* feature

`UpdateSalesOrder` (change the lines on a *draft* order) has few rules. We use it to practice *restraint*: a
feature can be small when the rules are thin.

- Input checks (at least one line, positive quantities) → the **validator**.
- The real *business* rule — "you can only edit a **draft** order" → a method on `SalesOrder`
  (`ReplaceLines` throws if not `Draft`), **not** the validator.
- Save via `ISalesOrderRepository`.

> 🚫 **Teaching moment — "when NOT to create a service":** you might reflexively add a `SalesOrderService`.
> **Don't.** There's a `SalesOrder` object and a handler; that's enough. A service here would be an empty
> pass-through — the anemic layer we're avoiding. Only add a service when Phase 5's test holds: *the logic
> spans multiple aggregates, or needs data the aggregate doesn't own.*

> 🗑️ **About `DeleteSalesOrder`:** in a real ERP you rarely *hard-delete* an order — you cancel it, keeping
> an auditable trail. If you do offer a delete, restrict it to `Draft` orders and make it a soft-delete
> (a flag), never a `DELETE` that erases history. The feature folder looks like every other; the domain rule
> ("only a draft can be deleted") lives on `SalesOrder`.

## Step 3 — Delivering domain events without a change tracker (the real mechanics)

ORM tutorials deliver domain events by hooking `SaveChanges`. **We have no `SaveChanges`** (we skipped the
ORM on purpose). So we deliver events *explicitly*.

**Option 1 — dispatch in the handler, after commit (recommended here).** After `tx.CommitAsync`, pull the
events off the aggregate and publish them. Simple, explicit, obvious ordering:

```csharp
await tx.CommitAsync(ct);
foreach (var e in order.DomainEvents) await publisher.Publish(e, ct);
order.ClearEvents();
```

**Option 2 — a dispatch step in the mediator pipeline.** A behavior that gathers events after the handler
finishes. DRYer, but needs machinery to *track* which aggregates were loaded. **Note it as the "next step,"
don't build it yet.**

**Publishing** uses a minimal notification contract in `Shared.Messaging`:

```csharp
public interface INotification { }   // domain events implement this
public interface INotificationHandler<in T> where T : INotification
{ Task Handle(T notification, CancellationToken ct); }
```

`IPublisher.Publish(e)` finds all `INotificationHandler<T>` for that event and runs them. Example: a
`SalesOrderSubmittedHandler` that writes an audit row — a *side effect*, kept out of the domain.

> 🔗 **Same idea, two names — don't let it trip you.** The [business tutorial](../plan-business-tutorial/08-approval-cancellation.md)
> builds this exact mechanism but names it after the *domain-event* vocabulary. The mapping is one-to-one:
> | Here (beginner) | Business tutorial | Role |
> |---|---|---|
> | `INotification` | *(the event implements `IDomainEvent`)* | the "something happened" record |
> | `INotificationHandler<T>` | `IDomainEventHandler<T>` | a reaction to that event |
> | `IPublisher` | `IDomainEventDispatcher` | finds the handlers and runs them after commit |
>
> Both dispatch domain events to zero-or-more handlers *after* the transaction commits. Pick one naming set
> in your own build and stay consistent — they are the same pattern, not two different ones.

> 💡 **Notification vs. command.** A **command** goes to exactly *one* handler and usually returns a result.
> A **notification** is an announcement to *any number* of handlers (zero to ten) and returns nothing.
> Command = "do this specific thing." Notification = "this happened, react if you care." *(Command is a
> phone call to one person; a notification is a bell everyone can hear.)*

### Events that cross the module wall (integration events)

Here's where notifications get powerful in a modular monolith. Suppose that when a Sales order is cancelled,
Inventory should release the stock — but you'd rather *not* have Sales call Inventory's contract inline.
Instead, Sales publishes `SalesOrderCancelled`, and **Inventory subscribes** with its own handler that
releases the stock. Sales doesn't know Inventory is listening; Inventory doesn't know Sales' internals. The
event *is* the contract.

- **Synchronous contract** (`IStockReservation`, Phase 5): use when the two must succeed or fail *together*
  in one transaction (submit *must* reserve, or don't submit).
- **Integration event** (`SalesOrderCancelled` → Inventory handler): use when the reaction can happen
  *just after*, best-effort, and you want the modules maximally decoupled.

Here's the whole decision on one screen — when a module needs to talk to another, ask these in order:

```
                       Must it succeed/fail in the SAME transaction as my change?
                                          │
                        ┌─────────── YES ─┴─ NO ───────────┐
                        ▼                                   ▼
             SYNCHRONOUS CONTRACT               Is it OK if it happens a moment later,
             (IStockReservation)                 and OK if it occasionally fails?
             e.g. submit MUST reserve                        │
             or the submit is off                ┌──── YES ──┴─ NO ─────────────┐
                                                 ▼                              ▼
                                       INTEGRATION EVENT            You need a transactional
                                       (publish; others react)      OUTBOX (reliable delivery)
                                       e.g. audit, notify           — a Phase 9 signpost, not
                                                                     built here
```

| You want… | Use | Coupling | Fails how? |
|---|---|---|---|
| "reserve, or don't submit" (atomic together) | **Synchronous contract** | tight, on purpose | the whole operation rolls back |
| "react afterwards" (audit, notify) | **Integration event** | loose | best-effort; reaction may be lost |
| "react afterwards, and it must *never* be lost" | **Outbox** (later) | loose + reliable | retried until delivered |

*Picking the right doorway for the coupling you actually have is the whole lesson of this section.*

For this project we release synchronously on cancel (it belongs in the same transaction), and use
integration events for genuine "react afterwards" side effects (audit, notifications). Knowing *which tool
for which coupling* is the lesson.

> ⚠️ **Honest trade-off:** because we publish *after* commit, if a notification handler fails, the order is
> *already* cancelled — the write stands, the reaction failed. Usually correct (don't un-cancel over a
> failed email!), but it means cross-module reactions via events are **best-effort**. Anything that must not
> be lost needs a **transactional outbox** (write the event in the same transaction, a relay delivers it with
> retries) — a signpost toward Phase 9, built only when a real requirement demands it.

## Step 4 — Completing an order (closing the state machine)

Add `SalesOrder.Complete()` (Submitted → Completed). Its handler **deducts** stock through the Inventory
contract (`DeductAsync`) — converting the earlier reservation into an actual reduction (Phase 5's
reserve→deduct policy). This closes the lifecycle and exercises the last transition. Thin feature, mirrors
`SubmitSalesOrder`.

## What to produce in this phase

- `CancelSalesOrder` feature (releases reservations through the contract, version-checked update).
- `UpdateSalesOrder` feature (thin; draft-only rule on `SalesOrder`, no service).
- `CompleteSalesOrder` feature (deducts stock through the contract).
- The enforced `SalesOrderStatus` transition table; `SalesOrder.Cancel`/`Complete`/`ReplaceLines` methods.
- `INotification`/`INotificationHandler`/`IPublisher` in `Shared.Messaging` + registration; after-commit
  dispatch in the command handlers; at least one notification handler (audit/log) to prove the path — and,
  optionally, an Inventory handler for a Sales integration event to prove cross-module reaction.
- Carter endpoints: `POST /sales-orders/{id}/cancel`, `POST /sales-orders/{id}/complete`,
  `PUT /sales-orders/{id}`.

**NOT now:** a full aggregate-tracking unit of work (note as Option 2); message queues / outbox; extracting
a module (Phase 9).

## Common mistakes to avoid

- **Dispatching events *before* commit.** A rollback then leaves handlers reacting to a change that never
  saved. Always dispatch after commit.
- **Assuming notification handlers are transactional.** They run after commit; reliable side effects need an
  outbox/queue.
- **Adding a `SalesOrderService` out of habit.** Re-apply the Phase 5 test before creating *any* service.
- **Skipping reservation release on cancel.** Leaves stock phantom-reserved.
- **Making Sales call Inventory's tables to release.** Go through `IStockReservation` (synchronous) or an
  integration event — never a direct reach.

---

## ✅ Check Yourself

1. **Why is the legality of cancellation decided by `order.Cancel()` and not the handler?**
2. **Why dispatch domain events *after* commit, and what's the consequence if a handler then fails?**
3. **When should a cross-module reaction be a synchronous contract call vs. an integration event?**
4. **Why does `UpdateSalesOrder` deliberately not get a service, and what test would justify one?**

---

## 👨‍🏫 Answer Key

**1. Why does `order.Cancel()` decide legality, not the handler?**
Because "can this order be cancelled?" is a **business rule about the order's own state** (Completed can't;
Cancelled can't again). It belongs on the entity via the `CanTransitionTo` state machine, where it has one
guardian and can't be forgotten. The handler only orchestrates side effects (release stock) and the
transaction.

**2. Why after commit, and the consequence of a later failure?**
So reactions fire only once the change is **durably saved**; dispatching before commit risks reacting to a
rolled-back change. Consequence: if a handler fails after commit, the write stands and only the reaction
failed — usually correct, but it makes reactions **best-effort**; must-not-lose reactions need an outbox.

**3. Synchronous contract vs. integration event?**
Use a **synchronous contract** (`IStockReservation`) when the two operations must succeed or fail *together*
in one transaction (submit *must* reserve). Use an **integration event** when the reaction can happen *just
after*, best-effort, and you want the modules maximally decoupled (audit, notify, or a non-transactional
release). Same wall, two doorways for two coupling needs.

**4. Why no service for `UpdateSalesOrder`, and what would justify one?**
A `SalesOrderService` here would be an empty pass-through — there's an aggregate and a handler, which is
enough; adding one re-introduces the anemic layer. A service is justified only when the logic **spans
multiple aggregates** or **needs data the aggregate doesn't own** (Phase 5's test). `UpdateSalesOrder` does
neither.

> ✅ **You're ready for Phase 8 if:** you notice the handlers all look the same by now (that repetition is the
> win), and you can explain *why events fire after commit* and *when to cross the wall synchronously vs. via
> an event*.
