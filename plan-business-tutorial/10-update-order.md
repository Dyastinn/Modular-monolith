# Lesson 10 — Update an Order: The Lost Update in the Lines

Good day, developer! Reps make mistakes, and customers change their minds, so a draft order needs to be
editable. Today we'll add that, and you'll see why it's short: the domain already knows how, and so does the
form. Then we'll find a
sneaky hole in last lesson's lost-update protection, one that only shows up when you change an order's lines
and nothing else. About an hour. Are you ready? Let's begin!

## What we'll build

- `PUT /api/sales-orders/{id}`: replace a **draft** order's lines, with every create-time rule checked again.
- A small change to `SalesDbContext` so two people editing the same draft can't silently overwrite each
  other.
- An **Edit** button, and the order form learning to edit as well as create.

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

internal sealed record UpdateSalesOrderCommand(long OrderId, IReadOnlyList<OrderLineInput> Lines) : ICommand;

internal sealed class UpdateSalesOrderValidator : AbstractValidator<UpdateSalesOrderCommand>
{
    public UpdateSalesOrderValidator()
    {
        RuleFor(x => x.OrderId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("An order needs at least one line.");
        RuleForEach(x => x.Lines).SetValidator(new OrderLineInputValidator());
    }
}

internal sealed class UpdateSalesOrderHandler(SalesDbContext db, ICurrentUser currentUser)
    : ICommandHandler<UpdateSalesOrderCommand>
{
    public async ValueTask<Unit> Handle(UpdateSalesOrderCommand cmd, CancellationToken ct)
    {
        var order = await db.GetVisibleOrderAsync(cmd.OrderId, new UserId(currentUser.UserId), currentUser.Role, ct);
        var customer = await db.Customers.SingleAsync(c => c.Id == order.CustomerId, ct);
        customer.EnsureNotBlocked();

        order.SetLines(await db.ResolveLinesAsync(cmd.Lines.Select(l => (l.ItemId, l.Quantity)), ct), currentUser.Role);
        customer.EnsureWithinCreditLimit(order.Total, await db.PendingTotalAsync(customer.Id, order.Id, ct));

        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
```

Six lines of real work, and every one is a call you've seen before. This is where lesson 03's choice pays
off. `SetLines` replaces the whole list and checks every rule on the full set, and it's also the "drafts
only" check: on a submitted order it throws before touching anything. `ResolveLinesAsync` from lesson 06
loads the items. Create and update share both, so there's no copy of the rules to drift apart.

The validator reuses `OrderLineInputValidator` from create, for the same reason: one line, one shape.

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Features/UpdateSalesOrder/UpdateSalesOrderEndpoint.cs
using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed record UpdateSalesOrderRequest(IReadOnlyList<OrderLineInput> Lines);

internal sealed class UpdateSalesOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPut("/sales-orders/{id:long}",
            async (long id, UpdateSalesOrderRequest body, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new UpdateSalesOrderCommand(id, body.Lines), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Sellers));
}
```

The order's id comes from the URL, and only the lines come in the body. That way there's never a question of
which id wins when the URL says 7 and the body says 8.

## 3. The form edits, too

The new-order page already has everything an edit needs: a customer, lines, a total. So the same page
answers a second address. Here's what changes in `OrderEditor.razor`.

Before:

```razor
@page "/sales-orders/new"
```

After:

```razor
@page "/sales-orders/new"
@page "/sales-orders/{Id:long}/edit"
```

A page may have several `@page` lines. On the second address, the router fills the `Id` parameter; on the
first, it stays `null`. So add the parameter, and let the title say which it is:

```razor
<PageTitle>@(Id is null ? "New order" : "Edit order") · ERP</PageTitle>

<MudText Typo="Typo.h4" Class="mb-4">@(Id is null ? "New sales order" : $"Edit {_orderNumber}")</MudText>
```

An order's customer never changes (a different customer is a different order), so the customer box is
locked when editing: add `Disabled="Id is not null"` to the customer `MudAutocomplete`. The Back button
goes back to wherever you came from: `Href="@(Id is null ? "sales-orders" : $"sales-orders/{Id}")"`.

Then the code. When editing, load the order and turn its lines back into form lines:

```csharp
[Parameter] public long? Id { get; set; }

private string? _orderNumber;

protected override async Task OnParametersSetAsync()
{
    if (Id is null) return;
    var order = await Api.Get<SalesOrderDetail>($"api/sales-orders/{Id}");
    if (order is null) return;
    _orderNumber = order.OrderNumber;
    _customer = await Api.Get<Customer>($"api/customers/{order.CustomerId}");
    var prices = await Api.Get<PagedResult<PriceListItem>>("api/price-list?forSaleOnly=true&pageSize=100");
    _lines = order.Lines.Select(l => new Line
    {
        Item = prices?.Items.FirstOrDefault(p => p.Id == l.ItemId), Quantity = l.Quantity
    }).ToList();
}
```

And `Save` creates or replaces:

```csharp
private async Task Save()
{
    var lines = _lines.Select(l => new { itemId = l.Item!.Id, quantity = l.Quantity }).ToList();
    if (Id is null)
    {
        if (await Api.Create("api/sales-orders", new { customerId = _customer!.Id, lines }, "Draft saved.") is { } newId)
            Nav.NavigateTo($"sales-orders/{newId}");
    }
    else if (await Api.Put($"api/sales-orders/{Id}", new { lines }, "Draft saved."))
    {
        Nav.NavigateTo($"sales-orders/{Id}");
    }
}
```

Look at how the lines are rebuilt. The order's lines carry the price *as ordered*; the form shows today's
price from the price list. If the price changed since the draft was made, the form shows the new total
before you save, and saving applies the new price. That's right for a draft, which nobody has committed to
yet.

Last, the button. On the order page, inside the `Draft` block, before Submit:

```razor
<MudButton Href="@($"sales-orders/{Id}/edit")" Variant="Variant.Outlined">Edit</MudButton>
```

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
- Letting the customer change on an existing order. A different customer is a new order.

## Let's run it!

```bash
TOKEN=$(curl -s -X POST http://localhost:5264/api/auth/login -H "Content-Type: application/json" \
  -d '{"email":"rep@erp.local","password":"Password123!"}' | jq -r .accessToken)

ID=$(curl -s -X POST http://localhost:5264/api/sales-orders -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"customerId":1,"lines":[{"itemId":1,"quantity":2}]}' | jq .id)

curl -s -o /dev/null -w "%{http_code}\n" -X PUT http://localhost:5264/api/sales-orders/$ID -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"lines":[{"itemId":2,"quantity":3}]}'
# 204: the draft now has 3 Gadgets instead of 2 Widgets

curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5264/api/sales-orders/$ID/submit -H "Authorization: Bearer $TOKEN"
curl -s -X PUT http://localhost:5264/api/sales-orders/$ID -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"lines":[{"itemId":1,"quantity":1}]}' | jq .detail
# "Lines can only be changed on a draft order."
```

In the app: open a draft, click **Edit**, swap the Widgets for Gadgets, and save. The order page shows the
new lines. Submit it, and the Edit button disappears.

The race itself is hard to time by hand with two terminals. [Lesson 12](12-testing.md) adds a test that loads
the same draft twice, saves both, and proves the second one fails.

## Let's check!

1. Why does update replace the whole list of lines, instead of patching it?
2. Ana and Ben edit the same draft's lines. Without the `SaveChangesAsync` override, why doesn't the `xmin`
   check catch it?
3. What exactly does the override change, and why is that enough?
4. What stops the handler from changing the lines of a submitted order?
5. Create and update share `SetLines` and `ResolveLinesAsync`. Why is that sharing in the domain and the
   DbContext, rather than a helper both handlers call?

And that's where this lesson ends. Drafts can be edited safely, even by two people at once, from the same
form that creates them. Next time, we make the system *react*: when an order is approved, something else
happens, reliably, even if the server crashes at the worst moment. Good luck, developer!

---

← [Lesson 09 — Approve, reject, cancel](09-approve-reject-cancel.md) · Next: [Lesson 11 — Events and the outbox](11-events-outbox.md) →
