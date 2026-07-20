# Phase 9 — Updating a Draft Order's Lines

> Self-contained: a familiar variation on the create slice ([Phase 6](06-create-order.md)), which you've
> already built. Money is in **Philippine Pesos (₱)**. If this phase feels repetitive, that's the point —
> the repeated shape is the maintainability win, and by the end you'll be able to place a new feature
> without thinking about where its pieces go.

---

> ⏱️ **Time for this phase:** ~45 min–1 hr. Deliberately short — it's a familiar variation on Create, and
> that familiarity *is* the point. If it feels quick, the pattern has landed.

## 1. What we're building

`PUT /sales-orders/{id}` — replace the lines on a **Draft** order. Only drafts can be edited (once
submitted, inventory is reserved and it's too late to freely change). Re-runs the same create-time
business checks against the new lines.

## 2. Why it's designed this way

- **Only drafts are editable.** This is a *domain* rule — `AddLine`/`ClearLines` already throw if the
  order isn't a Draft ([Phase 5](05-domain-model.md)). The handler doesn't re-check status; it just calls
  the domain and lets it refuse. That's the domain being authoritative.
- **Same checks as create.** New lines could introduce a restricted category, blow a quantity cap, or
  push over the credit limit — so we re-validate exactly as [Phase 6](06-create-order.md) did. We reuse
  the same domain methods, not copy their logic.
- **"Replace the lines" not "diff the lines."** For a draft with no reservations, the simplest correct
  approach is: clear the lines, add the new set. No clever merge needed. Simplicity is a feature.

## 3. The command and validator

```csharp
// src/Modules/Sales/Features/UpdateSalesOrder/UpdateSalesOrderHandler.cs
using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;        // NotFoundException, ICurrentUser
using Erp.Shared.BuildingBlocks;   // IDbConnectionFactory
using Erp.Shared.BuildingBlocks;
using FluentValidation;

namespace Erp.Modules.Sales.Features;

public sealed record UpdateSalesOrderCommand(
    long OrderId,
    IReadOnlyList<CreateSalesOrderLineDto> Lines) : ICommand<Unit>;   // reuse the line DTO from Phase 6

public sealed class UpdateSalesOrderValidator : AbstractValidator<UpdateSalesOrderCommand>
{
    public UpdateSalesOrderValidator()
    {
        RuleFor(x => x.OrderId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ItemId).GreaterThan(0);
            line.RuleFor(l => l.Quantity).GreaterThan(0);
        });
    }
}
```

## 4. The handler

```csharp
public sealed class UpdateSalesOrderHandler(
    ISalesOrderRepository orders,
    ICustomerRepository customers,
    IItemRepository items,
    IDbConnectionFactory factory,
    ICurrentUser currentUser) : ICommandHandler<UpdateSalesOrderCommand, Unit>   // command marker (Phase 2)
{
    public async Task<Unit> Handle(UpdateSalesOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetAsync(new SalesOrderId(cmd.OrderId), ct)
            ?? throw new NotFoundException($"Sales order {cmd.OrderId} not found.");

        // DOMAIN guards "draft only": ClearLines throws if the order isn't a Draft.
        order.ClearLines();

        var customer = await customers.GetAsync(order.CustomerId, ct)
            ?? throw new NotFoundException("Customer not found.");
        customer.EnsureNotBlocked();

        var itemIds = cmd.Lines.Select(l => l.ItemId).Distinct().ToList();
        var itemLookup = (await items.GetForOrderAsync(itemIds, ct)).ToDictionary(i => i.ItemId);
        var qtyByCategory = new Dictionary<long, int>();

        foreach (var line in cmd.Lines)
        {
            if (!itemLookup.TryGetValue(line.ItemId, out var item) || !item.IsActive)
                throw new NotFoundException($"Item {line.ItemId} not found or inactive.");

            item.Category.EnsureRoleAllowed(currentUser.Role);
            qtyByCategory.TryGetValue(item.Category.Id, out var running);
            running += line.Quantity;
            qtyByCategory[item.Category.Id] = running;
            item.Category.EnsureQuantityAllowed(running);

            order.AddLine(new ItemId(line.ItemId), new Quantity(line.Quantity), item.UnitPrice);
        }

        customer.EnsureWithinCreditLimit(order.Total);

        using var conn = factory.Create();
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        try
        {
            await orders.ReplaceLinesAsync(order, tx, ct);   // delete old lines, insert new, update total
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        return Unit.Value;
    }
}
```

> ❓ **You might be wondering: "This is almost identical to the create handler. Isn't that copy-paste I
> should refactor away?"** Fair instinct — but be careful. The two *look* similar yet answer different
> questions ("build a new order" vs "re-line an existing draft"), and premature "don't repeat yourself"
> often couples things that should evolve independently. A little duplication between two sibling slices
> is *healthier* than a shared "helper" that both are forced to bend around. The Vertical Slice
> philosophy explicitly tolerates this. If the shared logic were large and truly identical, you'd extract
> a small domain method (which we did — `EnsureRoleAllowed` etc. *are* the shared logic, and they live in
> the domain, not duplicated). What's left is orchestration, and that's fine to repeat.

> ❓ **You might be wondering: "Where's the status check for 'draft only'? The handler doesn't have
> one."** Exactly — and that's the point. `order.ClearLines()` and `order.AddLine()` throw if the order
> isn't a Draft. The rule lives in the domain, so the handler doesn't (and shouldn't) re-check it. If you
> catch yourself writing `if (order.Status != Draft)` here, delete it — the domain already guarantees it.

## 5. The repository `ReplaceLinesAsync`

> ℹ️ **First add this method to the `ISalesOrderRepository` interface** from
> [Phase 6 §4](06-create-order.md) — next to `AddAsync`/`GetAsync`/`UpdateStatusAsync`:
> `Task ReplaceLinesAsync(SalesOrder order, IDbTransaction tx, CancellationToken ct);`. The handler above
> calls it *through the interface*, so the interface has to declare it before this implementation will
> satisfy it. It's the one new repository method this slice needs.

```csharp
public async Task ReplaceLinesAsync(SalesOrder order, IDbTransaction tx, CancellationToken ct)
{
    var conn = tx.Connection!;
    // Delete existing lines, then insert the current set, then refresh the header total.
    await conn.ExecuteAsync(new CommandDefinition(
        "sales.usp_SalesOrderLine_DeleteByOrder", new { SalesOrderId = order.Id.Value },
        transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));

    foreach (var line in order.Lines)
        await conn.ExecuteAsync(new CommandDefinition(
            "sales.usp_SalesOrderLine_Insert",
            new { SalesOrderId = order.Id.Value, ItemId = line.ItemId.Value,
                  Quantity = line.Quantity.Value, UnitPrice = line.UnitPrice.Amount },
            transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));

    await conn.ExecuteAsync(new CommandDefinition(
        "sales.usp_SalesOrder_UpdateTotal",
        new { Id = order.Id.Value, TotalAmount = order.Total.Amount },
        transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));
}
```

All three steps — delete, insert, update total — run on the same transaction, so a failure leaves the
order's lines exactly as they were.

> ⚠️ **Notice what this update does *not* check: `RowVersion`.** [Phase 8](08-approval-cancellation.md) just
> taught you the version-checked `UPDATE` that stops a **lost update** — and `ReplaceLinesAsync` doesn't use
> it. That's a deliberate scope cut, and you should know exactly what it costs: if two sales reps open the
> same draft and both save, the second save silently wins. Nobody is told. The transaction still keeps each
> save *atomic* (you never get half a set of lines), but atomicity is not the same guarantee as "nobody
> overwrote me" — that was Phase 8's whole point.
>
> Closing it is a small, satisfying exercise and reuses machinery you already have: pass `order.RowVersion`
> into a version-checked `usp_SalesOrder_UpdateTotal` (`WHERE Id = @Id AND RowVersion = @RowVersion`), and
> throw `ConcurrencyException` on rowcount `0`, exactly as `UpdateStatusAsync` does. It's on the
> [Phase 13 checklist](13-wrap-up-checklist.md). We leave it out of the base flow only so this slice stays
> about *re-lining a draft* rather than re-teaching concurrency.

## 6. The endpoint (Carter module in the feature folder)

`Modules/Sales/Features/UpdateSalesOrder/UpdateSalesOrderEndpoint.cs`:

```csharp
public sealed class UpdateSalesOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPut("/sales-orders/{id:long}",
            async (long id, UpdateSalesOrderRequest body, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new UpdateSalesOrderCommand(id, body.Lines), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole("SalesRep", "SalesManager", "Admin"));
}

public sealed record UpdateSalesOrderRequest(IReadOnlyList<CreateSalesOrderLineDto> Lines);
```

> 💡 Notice the endpoint takes the id from the **route** (`{id}`) and the lines from the **body**, then
> combines them into the command. Keeping the id out of the body avoids the "id in the URL disagrees with
> id in the body" ambiguity — a small but real API best practice.

## 7. How the request flows

A SalesRep edits draft order #7, changing it to 3 Widgets:

```
1. Auth ok (SalesRep).
2. Validator: id > 0 ✓, 1 line ✓, qty 3 > 0 ✓.
3. Load order #7. order.ClearLines() → it's a Draft, so allowed.
   (If #7 were PendingApproval, ClearLines throws → 422 "Lines can only be changed on a draft order".)
4. Re-run checks: not blocked ✓, category role ✓, category cap ✓.
5. AddLine ×1 → total ₱75. EnsureWithinCreditLimit ✓.
6. BEGIN TRAN → delete old lines, insert new line, update total → COMMIT.
7. 204 No Content.
```

## 7b. Checkpoint — edit a draft, and prove a non-draft refuses ✅

Take a **Draft** order (from Phase 6), then change its lines:

```bash
DRAFT=2   # a Draft order's id

curl -X PUT "$BASE/sales-orders/$DRAFT" \
     -H "X-Api-Key: $KEY" -H "Authorization: Bearer $TOKEN" \
     -H "Content-Type: application/json" -d '{"lines":[{"itemId":1,"quantity":3}]}'   # → 204
```

`SELECT TotalAmount FROM sales.SalesOrder WHERE Id = 2;` now reads ₱75 (3 × ₱25). Then try the same
`PUT` against a **submitted** order and confirm it's refused with `422` "Lines can only be changed on a draft
order" — with its lines untouched. If a draft re-lines cleanly and a submitted order rejects the edit,
**Phase 9 is done and verifiable** — the domain, not the handler, is enforcing "draft only."

## 8. Why each component exists

| Component | Why |
|---|---|
| `UpdateSalesOrderCommand` | The order id + the new lines. |
| `UpdateSalesOrderValidator` | Input shape only. |
| `UpdateSalesOrderHandler` | Re-applies create-time rules to the new lines; relies on the domain for "draft only". |
| `ReplaceLinesAsync` | Atomically swaps the lines and refreshes the total. |

## 9. Common mistakes & best practices

- ❌ **A status `if` in the handler.** ✅ Let `ClearLines`/`AddLine` enforce "draft only" from the
  domain.
- ❌ **Skipping the re-checks** ("they passed at create"). ✅ New lines are new facts; re-validate role,
  cap, and credit.
- ❌ **Editing a submitted order's lines** (stock is already reserved). ✅ Domain refuses it; if the user
  needs changes, they cancel (releasing stock) and create anew — an intentional business flow.
- ❌ **Over-refactoring create/update into one "smart" method.** ✅ A little sibling-slice duplication is
  healthier than forced coupling; the *rules* are already shared (in the domain).
- ✅ **Best practice: id from route, data from body.** Avoids ambiguity and is conventional REST.

---

## ✅ Check Yourself

> Try each one before you look. [Model answers](ANSWER-KEY.md#phase-9-update-order) — but attempting first is what makes them stick.

1. Why doesn't the update handler contain a `if (status != Draft)` check?
2. Why re-run the credit/category checks on update instead of trusting the create-time result?
3. Why is a little duplication between the create and update handlers acceptable here?
4. Why take the id from the route and the lines from the body?

---

← Previous: [Phase 8 — Approval & cancellation](08-approval-cancellation.md) · [Tutorial overview](00-START-HERE.md) · Next: [Phase 10 — Queries: get one and search with paging](10-queries-search.md) →
