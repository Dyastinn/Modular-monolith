# Phase 7 — Finishing the Write Side: Cancel, Update, and "Something Happened" Events

> 📄 Expert version: [`../plan-Opus/08-phase-7-remaining-slices.md`](../plan-Opus/08-phase-7-remaining-slices.md)

**In one sentence:** We complete the remaining features (`CancelOrder`, `UpdateCustomer`,
`CompleteOrder`), enforce the full status state machine, and learn how "something happened" events
actually get delivered without an ORM to do it for us.

**What you'll build:** The `CancelOrder`, `UpdateCustomer`, and `CompleteOrder` features; the enforced
`OrderStatus` transition table; and a domain-event dispatch mechanism that runs *after* commit.

**Before you start:** Phases 4–6. You can create, book, and read orders.

---

## Step 1 — CancelOrder and the state machine

Cancelling isn't always allowed — a `Completed` order can't be cancelled, and a `Cancelled` one can't
be cancelled *again*. That's the state machine from Phase 3, now enforced for real:

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

        order.Cancel();   // DOMAIN decides if the transition is legal (throws if not)

        await using var tx = await uow.BeginAsync(ct);
        // If it was booked, give back the stock we reserved in Phase 5 (atomic SQL).
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
transaction.

> 💡 By now the handler should look *repetitive* — same shape as `BookOrder`. **That repetition is the
> win.** Every command reads the same way, so any developer can predict what a new one looks like.

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

## Step 2 — UpdateCustomer: a deliberately *simple* feature

Not every feature needs the full aggregate ceremony. `UpdateCustomer` (change name, contact) has very
few rules. We use it to practice *restraint*: a feature can be small when the rules are thin.

- Input checks (required fields, formats) → the **validator**.
- The one real *business* rule — e.g. "can't deactivate a customer with open booked orders" → a method
  on the `Customer` domain object, **not** the validator.
- Save via `ICustomerRepository`.

> 🚫 **Teaching moment — "when NOT to create a service":** you might reflexively add a
> `CustomerService`. **Don't.** There's a `Customer` object and a handler; that's enough. A service
> here would be an empty pass-through — the very anemic layer we're avoiding. Only add a service when
> Phase 5's test holds: *the logic spans multiple aggregates, or needs data the aggregate doesn't own.*

## Step 3 — Delivering domain events without a change tracker (the real mechanics)

ORM-based tutorials deliver domain events by hooking into `SaveChanges`. **We have no `SaveChanges`**
(we skipped the ORM on purpose). So we deliver events *explicitly*. Here are the honest options:

**Option 1 — dispatch in the handler, after commit (recommended here).** After `tx.CommitAsync`, pull
the events off the aggregate and publish them. Simple, explicit, and the ordering ("only after the
write is safely saved") is obvious:

```csharp
await tx.CommitAsync(ct);
foreach (var e in order.DomainEvents) await publisher.Publish(e, ct);
order.ClearEvents();
```

**Option 2 — a dispatch step in the mediator pipeline.** A behavior that, after the handler finishes,
gathers events from touched aggregates and publishes them. DRYer, but it needs a way to *track* which
aggregates were loaded — real extra machinery. **Note it as the "next step," don't build it yet.**

**Publishing** reuses the mediator's "notification" side. Add a minimal notification contract:

```csharp
public interface INotification { }   // domain events implement this
public interface INotificationHandler<in T> where T : INotification
{ Task Handle(T notification, CancellationToken ct); }
```

`IPublisher.Publish(e)` finds all the `INotificationHandler<T>` for that event and runs them. Example:
an `OrderBookedHandler` that writes an audit row or logs — a *side effect*, kept out of the domain.

> ⚠️ **An honest trade-off to understand:** because we publish *after* commit, if a notification
> handler fails, the order is *already booked* — the write succeeded, but the reaction (say, an email)
> failed. That's usually the **correct** behavior (don't un-book an order because an email server was
> down!) — but it means reactions must be handled out-of-band (retry/queue) rather than assumed to be
> part of the transaction. If a side effect *must* be reliable, that's a message-queue job, not an
> in-process event — and a signpost toward Phase 9.

## Step 4 — Completing an order (closing the state machine)

Add `Order.Complete()` (Booked → Completed). Its handler **deducts** `OnHand` — converting the earlier
reservation into an actual stock reduction (per Phase 5's reserve→deduct policy). This closes the order
lifecycle and exercises the last transition. Keep it a thin feature mirroring `BookOrder`.

## What to produce in this phase

- `CancelOrder` feature (releases reservations, version-checked update).
- `UpdateCustomer` feature (thin; business rule on `Customer`, no service).
- `CompleteOrder` feature (deducts stock).
- The enforced `OrderStatus` transition table; `Order.Cancel`/`Complete` methods.
- `INotification`/`INotificationHandler`/`IPublisher` + registration; after-commit dispatch in the
  command handlers; at least one notification handler (audit/log) to prove the path works.
- Endpoints: `POST /orders/{id}/cancel`, `POST /orders/{id}/complete`, `PUT /customers/{id}`.

**NOT now:** a full aggregate-tracking unit of work (note it as Option 2, don't build it); message
queues; cross-module events (Phase 9).

## Common mistakes to avoid

- **Dispatching events *before* commit.** Then a rollback leaves handlers having reacted to a change
  that never actually saved. Always dispatch after commit.
- **Assuming notification handlers are transactional.** They aren't (they run after commit). Reliable
  side effects need a queue, not a domain event.
- **Adding a `CustomerService`/`OrderService` out of habit.** Re-apply the Phase 5 test before creating
  *any* service.
- **Skipping reservation release on cancel.** Leaves stock phantom-reserved. Model
  reserve/deduct/release as a set.

---

## ✅ Check Yourself

1. **Why is the legality of cancellation decided by `order.Cancel()` and not the handler?**
2. **Why dispatch domain events *after* commit, and what's the consequence if a handler then fails?**
3. **Why does `UpdateCustomer` deliberately not get a service, and what test would justify one?**
4. **What's the difference between a best-effort in-process event and a side effect that *must* be
   reliable — and where does the latter push you architecturally?** *(Toward a queue/outbox — Phase 9.)*
