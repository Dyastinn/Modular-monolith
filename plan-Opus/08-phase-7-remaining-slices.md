# Phase 7 — Remaining Slices, State Machine & Domain Event Dispatch

**Goal:** complete the write side — `CancelSalesOrder`, `UpdateSalesOrder`, `CompleteSalesOrder`, the full
status state machine — and dispatch domain events without an EF change tracker, including across the module
wall.
**Produces:** the three slices (two files each), the enforced `SalesOrderStatus` transition table, and an
after-commit domain-event dispatch mechanism.
**Prereq:** Phases 4–6.

---

## Step 1 — CancelSalesOrder and the state machine

`Completed` can't cancel; `Cancelled` can't cancel again. Cancel releases the reservation — through the
Inventory contract, never a direct reach.

```csharp
public sealed record CancelSalesOrderCommand(long SalesOrderId) : ICommand<Unit>;

public sealed class CancelSalesOrderHandler(
    ISalesOrderRepository orders, IStockReservation stock, IUnitOfWork uow)
    : ICommandHandler<CancelSalesOrderCommand, Unit>
{
    public async Task<Unit> Handle(CancelSalesOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetAsync(new SalesOrderId(cmd.SalesOrderId), ct)
            ?? throw new NotFoundException($"Sales order {cmd.SalesOrderId} not found.");
        order.Cancel();   // domain: throws if the transition is illegal
        await using var tx = await uow.BeginAsync(ct);
        foreach (var line in order.Lines)
            await stock.ReleaseAsync(line.ProductId.Value, line.Quantity.Value, tx.Inner, ct);
        await orders.UpdateAsync(order, tx, ct);
        await tx.CommitAsync(ct);
        return Unit.Value;
    }
}
```

The handler decides nothing about legality — `order.Cancel()` does. Same shape as `SubmitSalesOrder`; the
repetition *is* the maintainability win.

**Transition table** (in `Sales/Domain`):

```csharp
private static readonly Dictionary<SalesOrderStatus, SalesOrderStatus[]> _allowed = new()
{
    [SalesOrderStatus.Draft]     = new[] { SalesOrderStatus.Submitted, SalesOrderStatus.Cancelled },
    [SalesOrderStatus.Submitted] = new[] { SalesOrderStatus.Completed, SalesOrderStatus.Cancelled },
    [SalesOrderStatus.Cancelled] = Array.Empty<SalesOrderStatus>(),
    [SalesOrderStatus.Completed] = Array.Empty<SalesOrderStatus>(),
};
public static bool CanTransitionTo(this SalesOrderStatus from, SalesOrderStatus to) => _allowed[from].Contains(to);
```

## Step 2 — UpdateSalesOrder (restraint)

Editing a *draft* order's lines has few invariants. Input checks in the validator; the one business rule
("only a draft can be edited") on `SalesOrder.ReplaceLines`; persist via the repository. **No
`SalesOrderService`** — an aggregate + handler is enough; a service would be the anemic trap. (Re-apply the
Phase 5 service test.) `DeleteSalesOrder`, if offered, is a draft-only soft-delete, not a hard `DELETE`.

## Step 3 — Domain event dispatch without a change tracker

**Option 1 — after-commit dispatch in the handler (recommended):**

```csharp
await tx.CommitAsync(ct);
foreach (var e in order.DomainEvents) await publisher.Publish(e, ct);
order.ClearEvents();
```

**Option 2 — a pipeline dispatch behavior** (needs an aggregate tracker). Note it; don't build it.

Notification contract in `Shared.Messaging`:

```csharp
public interface INotification { }
public interface INotificationHandler<in T> where T : INotification { Task Handle(T notification, CancellationToken ct); }
```

**Cross-module reactions (integration events):** publishing `SalesOrderCancelled` and letting **Inventory
subscribe** decouples further than a synchronous contract call. Use the **synchronous contract** when the
two must be in one transaction (submit *must* reserve); use an **integration event** for react-afterwards,
best-effort work (audit, notify). Keep domain events and integration events as **separate types** — internal
vs. published contract.

> **Tradeoff:** after-commit dispatch is best-effort — a failing handler doesn't roll back the write. Fine
> for audit/notify; must-not-lose reactions need a transactional outbox (Phase 9).

## Step 4 — CompleteSalesOrder

`SalesOrder.Complete()` (Submitted → Completed); the handler **deducts** stock via the contract
(`DeductAsync`). Thin slice mirroring `SubmitSalesOrder`.

## What to produce

- `CancelSalesOrder` (release via contract), `UpdateSalesOrder` (thin, no service), `CompleteSalesOrder`
  (deduct via contract) — two files each.
- Transition table; `Cancel`/`Complete`/`ReplaceLines` on `SalesOrder`.
- `INotification`/`INotificationHandler`/`IPublisher` in `Shared.Messaging`; after-commit dispatch; ≥1
  notification handler (audit) + optionally an Inventory handler for a Sales integration event.
- Carter endpoints: `POST /sales-orders/{id}/cancel`, `.../complete`, `PUT /sales-orders/{id}`.

**NOT:** aggregate-tracking UoW; queues/outbox; module extraction (Phase 9).

## Pitfalls

- Dispatching events before commit.
- Assuming notification handlers are transactional.
- Adding a `SalesOrderService` out of habit.
- Skipping reservation release on cancel.
- Reaching into `inventory.*` to release instead of using the contract/event.

---

## Checkpoint

1. Why does `order.Cancel()` decide legality, not the handler?
2. Why after commit, and the consequence of a failing handler?
3. Synchronous contract vs. integration event — when each?
4. Why no service for `UpdateSalesOrder`, and what would justify one?
