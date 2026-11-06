# Lesson 09 — Approve, Reject, Cancel: The Approval Workflow

Good day, developer! Orders can now be created and submitted, and small ones are approved on the spot. But a
big order just sits there, waiting for a manager. Today managers get their say: approve, or reject with a
reason. And anyone can cancel an order, giving its stock back. Along the way we'll meet a quiet bug that
loses money when two people act at once, and a hidden PostgreSQL column that stops it. About an hour and a
half. Are you ready? Let's begin!

## What we'll build

| Endpoint | Who | What happens |
|---|---|---|
| `POST /api/sales-orders/{id}/approve` | SalesManager, Admin | A pending order becomes Approved, and the customer is charged |
| `POST /api/sales-orders/{id}/reject` | SalesManager, Admin | A pending order becomes Rejected, with a reason; its stock is released |
| `POST /api/sales-orders/{id}/cancel` | SalesRep, SalesManager, Admin | The order becomes Cancelled; stock is released and any charge reversed |

Plus protection against **lost updates**: when two people change the same customer or the same order at the
same moment, the second one is told "someone else changed this, reload", instead of silently erasing the
first one's work. And the buttons for all three on the order page, with a dialog that asks for the
rejection reason.

## Why we build it this way

### The rules are already written

Look back at `SalesOrder` from [lesson 03](03-domain-model.md). `Approve`, `Reject` and `Cancel` are all
there, already tested: nobody approves their own order, a rejection needs a reason, cancelling an approved
order takes the charge back, and `Reject` and `Cancel` hand back the lines whose stock must be released. So
today's handlers are short. They load, call one domain method, and save.

### The lost update

Here's a bug that only shows up when two people act at once. Epsilon Retail owes ₱0. Two managers each
approve a different ₱60,000 order for Epsilon, at the same moment.

| Time | Manager A | Manager B |
|---|---|---|
| 1 | loads Epsilon: owes ₱0 | loads Epsilon: owes ₱0 |
| 2 | approves: owes ₱60,000 | approves: owes ₱60,000 |
| 3 | saves: owes ₱60,000 | saves: owes ₱60,000 |

Epsilon should owe ₱120,000. It owes ₱60,000. Manager B's save wrote over Manager A's, and nobody got an
error. That's a **lost update**, and in an ERP it's real money.

The same thing can happen to an order: one manager approves it while another rejects it, both from the same
"pending" copy, and whoever saves last wins.

### The fix: a version number on the row

PostgreSQL keeps a hidden column on every row, called **`xmin`**, and changes it every time the row is
updated. We tell EF Core to treat it as the row's **version**. Then, when EF saves, it doesn't just write
`UPDATE customer SET ... WHERE id = 5`; it writes `... WHERE id = 5 AND xmin = <the version I loaded>`.

So at time 3, Manager A's save matches and changes the row, and with it the `xmin`. Manager B's save looks
for the *old* version, matches nothing, and EF throws a `DbUpdateConcurrencyException`. We turn that into a
**409 Conflict**: "someone else changed this; reload and try again". Manager B reloads, sees ₱60,000 owed,
approves again, and Epsilon correctly owes ₱120,000.

To remember it easily: it's like editing a shared document that warns you "this file changed since you
opened it", instead of letting you overwrite it.

This style is called **optimistic concurrency**: we don't lock anything up front, we just detect the clash
at save time. It suits approvals, where clashes are rare. (In [lesson 08](08-submit.md) we *did* lock the
customer, because a credit check across many orders needed it.)

## 1. Map the version column

In `SalesDbContext`, add one line inside the `Customer` configuration and one inside the `SalesOrder`
configuration:

```csharp
e.Property<uint>("RowVersion").IsRowVersion().HasColumnName("xmin");
```

`RowVersion` is a **shadow property**: EF knows about it and tracks it, but our domain classes don't have it.
Versions are a database concern, so they stay out of the domain.

Create the migration:

```bash
dotnet ef migrations add AddConcurrencyTokens \
  --project src/Modules/Sales/Erp.Modules.Sales --startup-project src/Modules/Sales/Erp.Modules.Sales \
  --output-dir Infrastructure/Migrations
```

Open it and you'll see an `AddColumn` for `xmin`. Don't worry: PostgreSQL already has that column on every
table, and the provider knows not to create it. Applying this migration runs no SQL at all; it only records
the change in EF's model.

Then teach the exception handler the new answer. The host needs EF Core's exception type, and only that, so
it references EF Core itself rather than the PostgreSQL provider:

```bash
dotnet add src/Erp.Api package Microsoft.EntityFrameworkCore --version 10.0.14
```

In `GlobalExceptionHandler`, add `using Microsoft.EntityFrameworkCore;`, a line to the status switch, and a
line to the detail switch:

```csharp
DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Someone else changed this. Reload and try again."),
```

```csharp
StatusCodes.Status409Conflict                                     => null,
```

The title already says everything; EF's own message is about rows and versions, which means nothing to a
user.

## 2. Approve

Managers get their own line in `SalesRoles`:

```csharp
public static readonly string[] Managers = [Roles.SalesManager, Roles.Admin];
```

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Features/ApproveSalesOrder/ApproveSalesOrderHandler.cs
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Sales.Features;

internal sealed record ApproveSalesOrderCommand(long OrderId) : ICommand;

internal sealed class ApproveSalesOrderHandler(SalesDbContext db, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<ApproveSalesOrderCommand>
{
    public async ValueTask<Unit> Handle(ApproveSalesOrderCommand cmd, CancellationToken ct)
    {
        var approver = new UserId(currentUser.UserId);
        var order = await db.GetVisibleOrderAsync(cmd.OrderId, approver, currentUser.Role, ct);
        var customer = await db.Customers.SingleAsync(c => c.Id == order.CustomerId, ct);

        order.Approve(approver, customer, clock.UtcNow);

        await db.SaveChangesAsync(ct);   // order and customer balance: one transaction
        return Unit.Value;
    }
}
```

Notice there's no `BeginTransactionAsync` here. Two rows change (the order and the customer), but both are in
the same DbContext, and **one `SaveChangesAsync` is always one transaction**. We only start a transaction by
hand when another module has to join it, like Inventory did at submit.

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Features/ApproveSalesOrder/ApproveSalesOrderEndpoint.cs
using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed class ApproveSalesOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/sales-orders/{id:long}/approve",
            async (long id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new ApproveSalesOrderCommand(id), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Managers));
}
```

Two different checks keep approval safe, and they live in two different places:

| Rule | Where | Answer when broken |
|---|---|---|
| "Only managers approve" | The endpoint, `RequireRole` | 403 Forbidden, before the handler even runs |
| "Not your own order" | The domain, `SalesOrder.Approve` | 422, because it depends on the order's data |

## 3. Reject

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Features/RejectSalesOrder/RejectSalesOrderHandler.cs
using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore.Storage;

namespace Erp.Modules.Sales.Features;

internal sealed record RejectSalesOrderCommand(long OrderId, string Reason) : ICommand;

internal sealed class RejectSalesOrderValidator : AbstractValidator<RejectSalesOrderCommand>
{
    public RejectSalesOrderValidator()
    {
        RuleFor(x => x.OrderId).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

internal sealed class RejectSalesOrderHandler(SalesDbContext db, IStockReservation stock, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<RejectSalesOrderCommand>
{
    public async ValueTask<Unit> Handle(RejectSalesOrderCommand cmd, CancellationToken ct)
    {
        var manager = new UserId(currentUser.UserId);
        var order = await db.GetVisibleOrderAsync(cmd.OrderId, manager, currentUser.Role, ct);

        var toRelease = order.Reject(manager, cmd.Reason, clock.UtcNow);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var line in toRelease)
            await stock.ReleaseAsync(line.ItemId.Value, line.Quantity.Value, tx.GetDbTransaction(), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Unit.Value;
    }
}
```

The reason is checked twice, on purpose. The validator checks its *shape*: present, and at most 500
characters, to fit the column. The domain checks the *rule*: a rejection must have a real reason, so `"  "`
won't do. Releasing stock is Inventory's job again, so we're back to a transaction that Inventory joins.

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Features/RejectSalesOrder/RejectSalesOrderEndpoint.cs
using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed record RejectSalesOrderRequest(string Reason);

internal sealed class RejectSalesOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/sales-orders/{id:long}/reject",
            async (long id, RejectSalesOrderRequest body, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new RejectSalesOrderCommand(id, body.Reason), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Managers));
}
```

## 4. Cancel

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Features/CancelSalesOrder/CancelSalesOrderHandler.cs
using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Erp.Modules.Sales.Features;

internal sealed record CancelSalesOrderCommand(long OrderId) : ICommand;

internal sealed class CancelSalesOrderHandler(SalesDbContext db, IStockReservation stock, ICurrentUser currentUser)
    : ICommandHandler<CancelSalesOrderCommand>
{
    public async ValueTask<Unit> Handle(CancelSalesOrderCommand cmd, CancellationToken ct)
    {
        var order = await db.GetVisibleOrderAsync(cmd.OrderId, new UserId(currentUser.UserId), currentUser.Role, ct);
        var customer = await db.Customers.SingleAsync(c => c.Id == order.CustomerId, ct);

        var toRelease = order.Cancel(customer);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var line in toRelease)
            await stock.ReleaseAsync(line.ItemId.Value, line.Quantity.Value, tx.GetDbTransaction(), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Unit.Value;
    }
}
```

Look at how little the handler decides. A draft held no stock, so `Cancel` returns no lines. An approved order
had charged the customer, so `Cancel` already took the charge back. The handler just releases whatever it's
handed, and saves.

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Features/CancelSalesOrder/CancelSalesOrderEndpoint.cs
using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed class CancelSalesOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/sales-orders/{id:long}/cancel",
            async (long id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new CancelSalesOrderCommand(id), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Sellers));
}
```

A rep can cancel only an order they can see, so only their own: `GetVisibleOrderAsync` makes a rep's
attempt on anyone else's order a 404, without a line of extra code.

## 5. The buttons

Rejecting asks for a reason, and a reason needs somewhere to type it. A **dialog** is a small window over
the page; MudBlazor draws dialogs through a provider, so add it once to `MainLayout.razor`, next to the
others:

```razor
<MudDialogProvider />
```

Then a dialog we can reuse whenever a step needs a reason (voiding an invoice will need one too):

```razor
@* src/Erp.Web/Shared/ReasonDialog.razor *@
@* Asks for a reason before a step that needs one: reject, void, cancel. Returns the text, or nothing if cancelled. *@
<MudDialog>
    <DialogContent>
        <MudText Class="mb-3">@Message</MudText>
        <MudTextField @bind-Value="_reason" Label="Reason" Lines="3" MaxLength="500" Immediate="true" AutoFocus="true" />
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="() => Dialog.Cancel()">Back</MudButton>
        <MudButton Color="Color.Error" Variant="Variant.Filled" Disabled="string.IsNullOrWhiteSpace(_reason)"
                   OnClick="() => Dialog.Close(DialogResult.Ok(_reason.Trim()))">@Confirm</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = default!;
    [Parameter] public string Message { get; set; } = "";
    [Parameter] public string Confirm { get; set; } = "Confirm";
    private string _reason = "";

    public static async Task<string?> Ask(IDialogService dialogs, string title, string message, string confirm)
    {
        var parameters = new DialogParameters<ReasonDialog> { { d => d.Message, message }, { d => d.Confirm, confirm } };
        var result = await (await dialogs.ShowAsync<ReasonDialog>(title, parameters)).Result;
        return result is { Canceled: false, Data: string reason } ? reason : null;
    }
}
```

`MaxLength="500"` matches the validator, so the user can't type more than the API accepts. The `Ask`
helper hides MudBlazor's dialog plumbing: a page calls one line and gets the reason back, or `null` if the
user changed their mind.

On the order page, add `@inject IDialogService Dialogs` at the top, and the buttons under Submit:

```razor
@if (_order.Status == "PendingApproval")
{
    <AuthorizeView Roles="@Managers">
        <MudButton OnClick="Reject" Variant="Variant.Outlined" Color="Color.Error">Reject</MudButton>
        <MudButton OnClick="Approve" Variant="Variant.Filled" Color="Color.Success">Approve</MudButton>
    </AuthorizeView>
}
@if (_order.Status is "Draft" or "PendingApproval" or "Approved")
{
    <AuthorizeView Roles="@Sellers">
        <MudButton OnClick="Cancel" Variant="Variant.Text" Color="Color.Error">Cancel order</MudButton>
    </AuthorizeView>
}
```

A rejection belongs in the history, under the approval line:

```razor
@if (_order.RejectionReason is not null)
{
    <MudAlert Severity="Severity.Error" Dense="true" Class="mt-2">Rejected: @_order.RejectionReason</MudAlert>
}
```

And in `@code`:

```csharp
private static readonly string Managers = $"{Roles.SalesManager},{Roles.Admin}";

private Task Approve() => Act("approve", "Approved.");

private async Task Reject()
{
    if (await ReasonDialog.Ask(Dialogs, "Reject order", "Tell the sales rep why. The reserved stock is released.", "Reject") is { } reason
        && await Api.Post($"api/sales-orders/{Id}/reject", new { reason }, "Rejected."))
        await Load();
}

private async Task Cancel()
{
    var confirmed = await Dialogs.ShowMessageBoxAsync("Cancel order", "Cancel this order? Reserved stock is released and any charge is reversed.",
        yesText: "Cancel order", cancelText: "Keep it");
    if (confirmed == true) await Act("cancel", "Cancelled.");
}
```

Cancelling can't be undone, so it asks first. `ShowMessageBoxAsync` is MudBlazor's built-in yes/no box;
`true` means the user clicked "Cancel order".

Managers need to find the orders waiting for them. On the home page, next to the "New sales order" tile:

```razor
<AuthorizeView Roles="@ManagerRoles">
    <MudItem xs="12" sm="6" md="4">
        <Tile Title="Waiting for approval" Text="Orders above ₱50,000 that need you." Href="sales-orders?status=PendingApproval" Icon="@Icons.Material.Filled.HowToReg" />
    </MudItem>
</AuthorizeView>
```

with `private static readonly string ManagerRoles = $"{Roles.SalesManager},{Roles.Admin}";` in its `@code`.
That link opens the order list already filtered, thanks to the `[SupplyParameterFromQuery]` from lesson 07.

## How a request flows

```mermaid
sequenceDiagram
  participant M as Manager
  participant E as /approve
  participant H as ApproveSalesOrderHandler
  participant D as SalesOrder / Customer
  participant DB as PostgreSQL
  M->>E: POST /api/sales-orders/7/approve
  E->>E: RequireRole(SalesManager, Admin)
  E->>H: ApproveSalesOrderCommand(7)
  H->>DB: load order 7 and its customer (with their xmin)
  H->>D: order.Approve(manager, customer, now)
  H->>DB: UPDATE sales_order ... WHERE xmin = v1; UPDATE customer ... WHERE xmin = v2
  alt someone changed either row first
    DB-->>M: 409 Conflict
  else
    DB-->>M: 204 No Content
  end
```

## Mistakes to avoid

- Checking "not your own order" in the endpoint. It depends on the order's data, so it's a domain rule.
- Starting a transaction by hand when one `SaveChangesAsync` already covers everything.
- Releasing stock outside the transaction that changes the order's status. If the save fails, the stock is
  gone but the order still holds it.
- Showing EF's concurrency message to users. "Reload and try again" is all they need.
- Locking rows for every update "to be safe". Optimistic checks are cheaper, and right whenever clashes are
  rare.
- An irreversible button with no "are you sure?". Cancel asks first.

## Let's run it!

The approval rules need two different people, so sign in as both:

```bash
login() { curl -s -X POST http://localhost:5264/api/auth/login -H "Content-Type: application/json" \
  -d "{\"email\":\"$1\",\"password\":\"Password123!\"}" | jq -r .accessToken; }
REP=$(login rep@erp.local); MGR=$(login manager@erp.local)
```

The rep creates and submits a big order: one Server Rack, ₱75,000, for Epsilon.

```bash
ID=$(curl -s -X POST http://localhost:5264/api/sales-orders -H "Authorization: Bearer $REP" -H "Content-Type: application/json" \
  -d '{"customerId":5,"lines":[{"itemId":4,"quantity":1}]}' | jq .id)
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5264/api/sales-orders/$ID/submit -H "Authorization: Bearer $REP"
# 204 — and it stays PendingApproval, because it's over ₱50,000
```

Now try every door:

```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5264/api/sales-orders/$ID/approve -H "Authorization: Bearer $REP"   # 403: reps can't approve
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5264/api/sales-orders/$ID/approve -H "Authorization: Bearer $MGR"   # 204: approved
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5264/api/sales-orders/$ID/approve -H "Authorization: Bearer $MGR"   # 422: already approved
```

And the other paths: create and submit another rack order as the rep, then reject it as the manager with
`-d '{"reason":"Price not agreed"}'` (204), or cancel the approved one (204). In the database, Epsilon's
`outstanding_balance` goes up by ₱75,000 on approval and back down on cancel, and item 4's `reserved` goes up
on submit and back down on reject or cancel.

One more to try: as the manager, create *and submit* a rack order yourself, then approve it. You get a 422:
"An order can't be approved by the person who created it."

Now the way managers will really do it. Sign in to the app as `manager@erp.local` in one browser and as
`rep@erp.local` in another (or in a private window). As the rep, create a Server Rack order for Epsilon and
submit it. As the manager, click the **Waiting for approval** tile on the home page, open the order, and
click **Reject**. The dialog's Reject button stays grey until you type a reason. Type one and confirm; the
rep reloads the order and sees your reason in red.

## Let's check!

1. Describe a lost update in your own words, using two approvals for the same customer.
2. What does EF Core add to its `UPDATE` statement once `xmin` is a row version, and what happens when it
   matches no row?
3. Why does `ApproveSalesOrderHandler` need no `BeginTransactionAsync`, while `CancelSalesOrderHandler` does?
4. "Only managers approve" and "not your own order" live in different places. Where, and why?
5. Why is the rejection reason checked by both the validator and the domain?
6. When would you lock a row up front instead of using a version check?

And that's where this lesson ends. The full approval workflow works, and two people can no longer quietly
erase each other's changes. Next time, we let reps edit a draft, and find one more lost update hiding in the
order lines. Good luck, developer!

---

← [Lesson 08 — Submit](08-submit.md) · Next: [Lesson 10 — Update an order](10-update-order.md) →
