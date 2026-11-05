# Lesson 09 — Update an Order: The Lost Update in the Lines

Good day, developer! Reps make mistakes, and customers change their minds, so a draft order needs to be
editable. Today we'll add that, and you'll see why it's short: the domain already knows how. Then we'll find a
sneaky hole in last lesson's lost-update protection, one that only shows up when you change an order's lines
and nothing else. About an hour. Are you ready? Let's begin!

## What we'll build

- `PUT /sales-orders/{id}`: replace a **draft** order's lines, with every create-time rule checked again.
- A small change to `SalesDbContext` so two people editing the same draft can't silently overwrite each
  other.

## Why we build it this way

### Replace, don't patch

The request carries the order's complete new list of lines, and we replace the old ones. That's simpler and
safer than sending "add this, remove that, change the other" instructions: there's no way for the client and
server to disagree about what the order looks like afterwards.

And every rule runs again: blocked customer, category limits, credit. A draft that was fine yesterday might
not be today.

### The hole in the version check

Last lesson, we made EF Core check each row's `xmin` version when it saves. Now picture this. Ana and Ben
both open the same draft. Ana replaces its lines and saves. Ben replaces them too, and saves a second later.

What does EF Core write when the lines change? It deletes the old rows in `sales_order_line` and inserts the
new ones. **It never updates the `sales_order` row itself**, because nothing on the order changed, only its
lines. And the version check only runs on rows that get an `UPDATE`. So Ben's save goes straight through,
Ana's lines vanish, and nobody is told. Clear so far?

The fix: whenever an order's lines change, also touch the order's own row. Then the order gets an `UPDATE`,
the `xmin` check runs, and Ben gets a 409.

## 1. Touch the order when its lines change

In `SalesDbContext`, override `SaveChangesAsync`:

```csharp
public override Task<int> SaveChangesAsync(CancellationToken ct = default)
{
    // A lines-only edit never updates the order row, so its xmin would never be checked. Touch it.
    if (ChangeTracker.Entries<SalesOrderLine>().Any(l => l.State != EntityState.Unchanged))
        foreach (var order in ChangeTracker.Entries<SalesOrder>().Where(o => o.State == EntityState.Unchanged))
            order.Property(o => o.Status).IsModified = true;

    return base.SaveChangesAsync(ct);
}
```

The **change tracker** is EF Core's notebook of everything it loaded and what happened to it since. If any
line was added, deleted or modified, we mark the order's `Status` as modified. The value doesn't actually
change, but EF now writes `UPDATE sales_order SET status = ... WHERE id = ... AND xmin = ...`, and that's
all we need: the version check runs.

To remember it easily: changing a page in a book should change the book's edition number too.

## 2. The update slice

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Features/UpdateSalesOrder/UpdateSalesOrderHandler.cs
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Sales.Features;

internal sealed record UpdateSalesOrderCommand(long OrderId, IReadOnlyList<CreateSalesOrderLineDto> Lines) : ICommand;

internal sealed class UpdateSalesOrderValidator : AbstractValidator<UpdateSalesOrderCommand>
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

internal sealed class UpdateSalesOrderHandler(SalesDbContext db, ICurrentUser currentUser)
    : ICommandHandler<UpdateSalesOrderCommand>
{
    public async ValueTask<Unit> Handle(UpdateSalesOrderCommand cmd, CancellationToken ct)
    {
        var orderId = new SalesOrderId(cmd.OrderId);
        var order = await db.SalesOrders.SingleOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new NotFoundException($"Sales order {cmd.OrderId} not found.");

        order.ClearLines();

        var customer = await db.Customers.SingleAsync(c => c.Id == order.CustomerId, ct);
        customer.EnsureNotBlocked();

        var itemIds = cmd.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.Include(i => i.Category)
            .Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);

        var qtyByCategory = new Dictionary<long, int>();
        foreach (var line in cmd.Lines)
        {
            if (!items.TryGetValue(line.ItemId, out var item) || !item.IsActive)
                throw new NotFoundException($"Item {line.ItemId} not found or inactive.");

            item.Category.EnsureRoleAllowed(currentUser.Role);
            qtyByCategory[item.CategoryId] = qtyByCategory.GetValueOrDefault(item.CategoryId) + line.Quantity;
            item.Category.EnsureQuantityAllowed(qtyByCategory[item.CategoryId]);

            order.AddLine(new ItemId(item.Id), new Quantity(line.Quantity), item.UnitPrice);
        }

        customer.EnsureWithinCreditLimit(order.Total, await db.PendingTotalAsync(customer.Id, order.Id, ct));

        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
```

`order.ClearLines()` comes first for a reason: it's also the "drafts only" check. If the order has been
submitted, the domain throws right there, before we load anything else. The line loop is the same as in
create; the order just already exists.

You may notice the line-building loop is a copy of the one in create. That's a deliberate choice of vertical
slices: each feature owns its code, so changing how update works can never break create. If the copies ever
need to change *together*, that's the signal to move the shared part into the domain.

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Features/UpdateSalesOrder/UpdateSalesOrderEndpoint.cs
using Carter;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed record UpdateSalesOrderRequest(IReadOnlyList<CreateSalesOrderLineDto> Lines);

internal sealed class UpdateSalesOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPut("/sales-orders/{id:long}",
            async (long id, UpdateSalesOrderRequest body, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new UpdateSalesOrderCommand(id, body.Lines), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(Roles.SalesRep, Roles.SalesManager, Roles.Admin));
}
```

The order's id comes from the URL, and only the lines come in the body. That way there's never a question of
which id wins when the URL says 7 and the body says 8.

## How a request flows

```mermaid
sequenceDiagram
  participant A as Ana
  participant B as Ben
  participant DB as PostgreSQL
  A->>DB: load draft 7 (xmin = 100)
  B->>DB: load draft 7 (xmin = 100)
  A->>DB: delete/insert lines; UPDATE sales_order ... WHERE xmin = 100  → 1 row (xmin becomes 101)
  B->>DB: delete/insert lines; UPDATE sales_order ... WHERE xmin = 100  → 0 rows
  DB-->>B: 409 Conflict: reload and try again
```

## Mistakes to avoid

- Accepting "patch" instructions for lines. Replacing the whole list is simpler, and nothing can drift.
- Skipping the rules on update because they "already ran on create". The customer may be blocked today.
- Trusting the version check to cover child rows. It only checks rows that get an `UPDATE`.
- Taking the order id from the body as well as the URL.

## Let's run it!

```bash
TOKEN=$(curl -s -X POST http://localhost:5264/auth/login -H "Content-Type: application/json" \
  -d '{"email":"rep@erp.local","password":"Password123!"}' | jq -r .accessToken)

ID=$(curl -s -X POST http://localhost:5264/sales-orders -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"customerId":1,"lines":[{"itemId":1,"quantity":2}]}' | jq .id)

curl -s -o /dev/null -w "%{http_code}\n" -X PUT http://localhost:5264/sales-orders/$ID -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"lines":[{"itemId":2,"quantity":3}]}'
# 204: the draft now has 3 Gadgets instead of 2 Widgets

curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5264/sales-orders/$ID/submit -H "Authorization: Bearer $TOKEN"
curl -s -X PUT http://localhost:5264/sales-orders/$ID -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"lines":[{"itemId":1,"quantity":1}]}' | jq .detail
# "Lines can only be changed on a draft order."
```

The race itself is hard to time by hand with two terminals. [Lesson 14](14-testing.md) adds a test that loads
the same draft twice, saves both, and proves the second one fails.

## Let's check!

1. Why does update replace the whole list of lines, instead of patching it?
2. Ana and Ben edit the same draft's lines. Without the `SaveChangesAsync` override, why doesn't the `xmin`
   check catch it?
3. What exactly does the override change, and why is that enough?
4. Why is `order.ClearLines()` the first thing the handler does to the order?
5. Create and update share a loop of code. Why is that acceptable here, and when would it stop being?

And that's where this lesson ends. Drafts can be edited safely, even by two people at once. Next time, we
change direction: instead of changing data, we'll read it, fast, for screens and searches. Good luck,
developer!

---

← [Lesson 08 — Approve, reject, cancel](08-approve-reject-cancel.md) · Next: [Lesson 10 — Reads](10-reads.md) →
