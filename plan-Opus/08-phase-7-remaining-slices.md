# Phase 7 — Remaining Slices, State Machine & Domain Event Dispatch

**Goal:** complete the write side — `CancelOrder`, `UpdateCustomer`, the full status state machine —
and implement how domain events actually get dispatched without an EF change tracker.
**Produces:** `CancelOrder` and `UpdateCustomer` slices, the enforced `OrderStatus` transition table,
and a domain-event dispatch mechanism that runs after commit.
**Prereq:** Phases 4–6. You can create, book, and read orders.

---

## Step 1 — CancelOrder and the state machine

Cancelling isn't always allowed — a `Completed` order can't be cancelled; a `Cancelled` one can't be
cancelled again. This is the state machine from Phase 3, now enforced end-to-end.

```csharp
public sealed record CancelOrderCommand(long OrderId) : ICommand<Unit>;

public sealed class CancelOrderHandler(
    IOrderRepository orders, IInventoryService inventory, IUnitOfWork uow)
    : IRequestHandler<CancelOrderCommand, Unit>
{
    public async Task<Unit> Handle(CancelOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetAsync(new OrderId(cmd.OrderId), ct)
            ?? throw new NotFoundException($"Order {cmd.OrderId} not found.");

        order.Cancel();   // domain: throws if the transition is illegal (Phase 3 state machine)

        await using var tx = await uow.BeginAsync(ct);
        // If it was booked, release the reservations we made in Phase 5 (atomic SQL).
        foreach (var line in order.Lines)
            await inventory.Release(line.MaterialId, line.Quantity, tx, ct);
        await orders.UpdateAsync(order, tx, ct);   // version-checked status update
        await tx.CommitAsync(ct);

        return Unit.Value;   // OrderCancelled event dispatched after commit (Step 3)
    }
}
```

The handler decides *nothing* about whether cancellation is legal — `order.Cancel()` does, via
`CanTransitionTo`. The handler only orchestrates the side effects (release reservations) and the
transaction. Same shape as `BookOrder`; by now the template should feel repetitive — that repetition
*is* the maintainability win (every command reads the same way).

**Enforce the transition table** so illegal moves are impossible:

```csharp
private static readonly Dictionary<OrderStatus, OrderStatus[]> _allowed = new()
{
    [OrderStatus.Draft]     = new[] { OrderStatus.Booked, OrderStatus.Cancelled },
    [OrderStatus.Booked]    = new[] { OrderStatus.Completed, OrderStatus.Cancelled },
    [OrderStatus.Cancelled] = Array.Empty<OrderStatus>(),
    [OrderStatus.Completed] = Array.Empty<OrderStatus>(),
};
public static bool CanTransitionTo(this OrderStatus from, OrderStatus to) =>
    _allowed[from].Contains(to);
```

## Step 2 — UpdateCustomer (a simpler slice, deliberately)

Not every use case needs the full aggregate ceremony. `UpdateCustomer` (name, contact) has few
invariants. Use it to show restraint: a slice can be small when the domain rules are thin.

- Input validation in the validator (required fields, formats).
- The one *business* rule — e.g. "cannot deactivate a customer with open booked orders" — lives on the
  `Customer` domain method (or a small check the handler asks the domain to make), **not** the validator.
- Persist via `ICustomerRepository`.

> **Teaching point — "when NOT to create a service":** you might reflexively add a `CustomerService`.
> Don't. There's a `Customer` aggregate and a handler; that's enough. A service here would be an empty
> pass-through — the anemic layer the brief tells you to avoid. Add a service only when Phase 5's test
> holds (logic spans aggregates or needs data the aggregate doesn't own).

## Step 3 — Domain event dispatch without a change tracker (the real mechanics)

EF-based tutorials dispatch domain events by hooking `SaveChanges`. You have no `SaveChanges`. So you
dispatch explicitly. The honest options, with tradeoffs:

**Option 1 — dispatch in the handler after commit (recommended for this project).** After
`tx.CommitAsync`, pull events off the aggregate and publish them. Simple, explicit, and the ordering
("only after the write is durable") is obvious.

```csharp
await tx.CommitAsync(ct);
foreach (var e in order.DomainEvents) await publisher.Publish(e, ct);
order.ClearEvents();
```

**Option 2 — a dispatch step in the mediator pipeline.** A behavior that, after the handler returns,
collects events from aggregates and publishes them. DRYer, but it needs a way to *find* the aggregates
that were touched (an ambient "unit of work tracks loaded aggregates" mechanism), which is real
machinery. Note it as the "next step" but don't build it yet.

**Publishing** reuses your mediator's notification side. Add a minimal notification contract:

```csharp
public interface INotification { }   // domain events implement this (or wrap them)
public interface INotificationHandler<in T> where T : INotification
{ Task Handle(T notification, CancellationToken ct); }
```

`IPublisher.Publish(e)` resolves all `INotificationHandler<T>` and invokes them. Example handler:
`OrderBookedHandler` that (say) writes an audit row or logs — a *side effect*, kept out of the domain.

> **Tradeoff to state:** publishing after commit means if a notification handler throws, the write is
> already durable — the event handling failed but the order is booked. That's usually the *correct*
> semantics (don't roll back a booking because an email failed), but you must handle notification
> failures out-of-band (retry/queue) rather than assuming they're transactional. For in-process,
> best-effort side effects this is fine; if a side effect *must* be reliable, that's a message-queue
> concern, not an in-process domain event — and a signpost toward Phase 9.

## Step 4 — Completing an order (closing the state machine)

Add `Order.Complete()` (Booked → Completed) which, in its handler, **deducts** `OnHand` (converting the
reservation to an actual stock reduction, per Phase 5's reserve→deduct policy). This closes the
lifecycle and exercises the last transition. Keep it a thin slice mirroring `BookOrder`.

## What Sonnet should produce

- `CancelOrder` slice (releases reservations, version-checked update).
- `UpdateCustomer` slice (thin; business rule on the `Customer` domain, no service).
- `CompleteOrder` slice (deducts stock).
- Enforced `OrderStatus` transition table; `Order.Cancel`/`Complete` methods.
- `INotification`/`INotificationHandler`/`IPublisher` + registration; after-commit dispatch in the
  command handlers; at least one notification handler (audit/log) to prove the path.
- Endpoints: `POST /orders/{id}/cancel`, `POST /orders/{id}/complete`, `PUT /customers/{id}`.

**NOT in this phase:** a full unit-of-work aggregate tracker (note it as Option 2, don't build it);
message queues; cross-module events (Phase 9).

## Pitfalls

- **Dispatching events before commit.** Then a rollback leaves handlers having reacted to a change that
  didn't persist. Dispatch after commit.
- **Assuming notification handlers are transactional.** They're not (they run post-commit). Reliable
  side effects need a queue, not a domain event.
- **Adding a `CustomerService`/`OrderService`** out of habit. Re-apply the Phase 5 test before creating
  any service.
- **Skipping reservation release on cancel.** Leaves stock phantom-reserved (see Phase 5 set:
  reserve/deduct/release).

---

## Checkpoint — you understand this phase when you can answer:

1. Why is the legality of cancellation decided by `order.Cancel()` and not the handler?
2. Why dispatch domain events *after* commit, and what's the consequence for a failing handler?
3. Why does `UpdateCustomer` deliberately not get a service, and what test would justify one?
4. What's the difference between a best-effort in-process domain event and a side effect that must be
   reliable — and where does the latter push you architecturally?
