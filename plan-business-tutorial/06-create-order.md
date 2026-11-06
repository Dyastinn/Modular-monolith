# Lesson 06 — Create an Order: The First Real Feature

Good day, developer! Everything we've built so far has been foundation: walls, a mediator, the rules, a
database, sign-in, and an app to put it all in. Today it all meets in one place. A sales rep will send an order, and the system will check
the customer, the items, the categories and the credit limit, then save it with a proper order number. And
when something's wrong, the caller will get a clear answer instead of a crash. About two hours. Are you
ready? Let's begin!

## What we'll build

- `POST /api/sales-orders`: create a draft order, for SalesRep, SalesManager and Admin.
- Order numbers like `SO-2026-000042`, which no two orders can ever share.
- Proper error responses: 400 for a malformed request, 404 for something missing, 422 for a broken business
  rule.
- Swagger, a page in the browser where you can try every endpoint.
- The first screen: a **New sales order** form in the app.

We'll also say goodbye to `/ping`. It did its job.

## Why we build it this way

### A handler with no business `if`s

The handler coordinates; the domain decides. Here's the whole job, in order:

1. Load the customer, and ask the customer whether it may order.
2. Load the items with their categories.
3. Create the order and hand it the lines. `SetLines` from lesson 03 checks every item, role and category
   cap.
4. Ask the customer whether the total fits its credit.
5. Save.

Every "is this allowed?" is a method call on a domain object. If a rule changes, the handler doesn't.

### Errors the caller can understand

Run lesson 02's checkpoint again and you'll remember the problem: a failed validation came back as a
**500 Internal Server Error**, with a stack trace. That's wrong in two ways. A 500 means "the server broke",
when really the caller sent a bad request. And a stack trace tells an attacker far too much.

There's a standard for error responses, **Problem Details** (RFC 9457): a small JSON body with a `status`,
a `title` and a `detail`. We'll write one exception handler that turns each kind of exception into the right
status, once, for every endpoint.

| What went wrong | Exception | HTTP status |
|---|---|---|
| The request is malformed (no lines, a negative quantity) | `ValidationException` | 400 Bad Request |
| Something it refers to doesn't exist | `NotFoundException` | 404 Not Found |
| A business rule said no (blocked customer, over the credit limit) | `DomainException` | 422 Unprocessable Content |
| Anything else: a real bug | any other | 500, with no internals shown in production |

## 1. Two small additions to BuildingBlocks

A handler shouldn't call `DateTime.UtcNow` directly, for the same reason the domain doesn't: you can't control
it in a test. So handlers ask for a clock:

```csharp
// src/Shared/Erp.Shared.BuildingBlocks/Abstractions/IClock.cs
namespace Erp.Shared.BuildingBlocks;

// Inject instead of DateTime.UtcNow so time-based rules can be tested with a fixed clock.
public interface IClock
{
    DateTime UtcNow { get; }
}
```

```csharp
// src/Shared/Erp.Shared.Infrastructure/SystemClock.cs
using Erp.Shared.BuildingBlocks;

namespace Erp.Shared.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
```

Register it in `AddSharedInfrastructure`: `services.AddSingleton<IClock, SystemClock>();`

And a way to say "that doesn't exist":

```csharp
// src/Shared/Erp.Shared.BuildingBlocks/Abstractions/Exceptions.cs
namespace Erp.Shared.BuildingBlocks;

// Mapped to HTTP 404 by GlobalExceptionHandler.
public class NotFoundException(string message) : Exception(message);
```

And one for document numbers. Sales orders need them today; purchase orders and invoices will want the same
shape later, so it lives where every module can use it:

```csharp
// src/Shared/Erp.Shared.BuildingBlocks/Domain/DocumentNumber.cs
namespace Erp.Shared.BuildingBlocks;

// SO-2026-000042: a prefix people recognize, the year, and a counter that only goes up.
public static class DocumentNumber
{
    public static string Format(string prefix, long sequenceValue, DateTime nowUtc) =>
        $"{prefix}-{nowUtc:yyyy}-{sequenceValue:D6}";
}
```

## 2. Order numbers no two orders can share

The naive way to number orders is "find the highest number and add one". Picture two reps clicking Save at
the same instant: both read 41, both write 42. A **sequence** fixes that. It's a counter inside PostgreSQL,
and `nextval()` hands each caller a different number, even when a thousand ask at once.

In `SalesDbContext.OnModelCreating`, right after `HasDefaultSchema`:

```csharp
b.HasSequence<long>("sales_order_number_seq");
```

And add a method that takes the next number and formats it:

```csharp
public async Task<string> NextOrderNumberAsync(DateTime nowUtc, CancellationToken ct)
{
    // ToList, not Single(): EF would wrap Single() in a subquery.
    var next = (await Database
        .SqlQuery<long>($"SELECT nextval('sales.sales_order_number_seq') AS \"Value\"")
        .ToListAsync(ct)).Single();
    return DocumentNumber.Format("SO", next, nowUtc);
}
```

`SqlQuery<long>` runs a raw query and reads a single column named `Value`. The database supplies the number;
`DocumentNumber` decides how it looks.

The handler also needs to turn "item 1, quantity 2" from the request into a real `SalesItem` for
`SetLines`. Creating an order needs it, and so will editing one, so it's a method on the context too:

```csharp
// Pairs each requested line with its item. An unknown item is a 404; whether it may be sold is the order's call.
public async Task<IReadOnlyList<(SalesItem Item, Quantity Quantity)>> ResolveLinesAsync(
    IEnumerable<(long ItemId, decimal Quantity)> lines, CancellationToken ct)
{
    var requested = lines.ToList();
    var ids = requested.Select(l => new ItemId(l.ItemId)).Distinct().ToList();
    var items = await SalesItems.Include(i => i.Category).Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id.Value, ct);
    return requested
        .Select(l => (items.TryGetValue(l.ItemId, out var item) ? item : throw new NotFoundException($"Item {l.ItemId} not found."),
                      new Quantity(l.Quantity)))
        .ToList();
}
```

One query loads every item on the order with its category (`Include`), instead of one trip per line. Notice
what it *doesn't* check: whether the item is for sale, or whether you may sell it. Those are business rules,
and `SetLines` already asks the item and its category.

One more method, for the credit rule. The customer's limit has to cover their orders still awaiting
approval. Nothing can be awaiting approval yet (submitting comes next lesson), but the rule is already in the
domain, so we honor it from the start:

```csharp
// The customer's other submitted orders still waiting for a manager. They already count against credit.
public async Task<Money> PendingTotalAsync(CustomerId customerId, SalesOrderId excluding, CancellationToken ct)
{
    var pending = await SalesOrders.AsNoTracking()
        .Where(o => o.CustomerId == customerId && o.Status == SalesOrderStatus.PendingApproval && o.Id != excluding)
        .ToListAsync(ct);
    return pending.Aggregate(Money.Zero, (sum, o) => sum + o.Total);
}
```

`AsNoTracking()` tells EF we're only reading these orders, so it doesn't need to watch them for changes. The
total is computed in C# by the domain's own `Total`, so it can't disagree with how the domain counts.

Create a migration for the sequence:

```bash
dotnet ef migrations add AddOrderNumberSequence \
  --project src/Modules/Sales/Erp.Modules.Sales --startup-project src/Modules/Sales/Erp.Modules.Sales \
  --output-dir Infrastructure/Migrations
```

## 3. The slice

The ping feature has done its job: delete the `Features/Ping` folder, the app's `Pages/Ping.razor`, and its link in `NavMenu.razor`. Then create `Features/CreateSalesOrder/`:

Two small helpers first. Many requests will carry quantities and page numbers, so their shape rules are
written once, in BuildingBlocks:

```csharp
// src/Shared/Erp.Shared.BuildingBlocks/Messaging/ValidationRules.cs
using FluentValidation;

namespace Erp.Shared.BuildingBlocks;

// Shape rules that many requests share, written once.
public static class ValidationRules
{
    // Up to three decimal places; whether fractions are allowed at all is the item's business, not the request's.
    public static IRuleBuilderOptions<T, decimal> ValidQuantity<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.GreaterThan(0).Must(q => decimal.Round(q, 3) == q).WithMessage("Quantity allows at most three decimal places.");
}
```

And who may sell, decided once for every Sales endpoint:

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Features/SalesRoles.cs
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Features;

// Who may do what in Sales, decided once for every endpoint.
internal static class SalesRoles
{
    public static readonly string[] Sellers = [Roles.SalesRep, Roles.SalesManager, Roles.Admin];
}
```

Now the slice itself:

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Features/CreateSalesOrder/CreateSalesOrderHandler.cs
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Sales.Features;

internal sealed record OrderLineInput(long ItemId, decimal Quantity);

internal sealed record CreateSalesOrderCommand(long CustomerId, IReadOnlyList<OrderLineInput> Lines) : ICommand<long>;

internal sealed class OrderLineInputValidator : AbstractValidator<OrderLineInput>
{
    public OrderLineInputValidator()
    {
        RuleFor(l => l.ItemId).GreaterThan(0);
        RuleFor(l => l.Quantity).ValidQuantity();
    }
}

internal sealed class CreateSalesOrderValidator : AbstractValidator<CreateSalesOrderCommand>
{
    public CreateSalesOrderValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("An order needs at least one line.");
        RuleForEach(x => x.Lines).SetValidator(new OrderLineInputValidator());
    }
}

internal sealed class CreateSalesOrderHandler(SalesDbContext db, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<CreateSalesOrderCommand, long>
{
    public async ValueTask<long> Handle(CreateSalesOrderCommand cmd, CancellationToken ct)
    {
        var customerId = new CustomerId(cmd.CustomerId);
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == customerId, ct)
            ?? throw new NotFoundException($"Customer {cmd.CustomerId} not found.");
        customer.EnsureNotBlocked();

        var lines = await db.ResolveLinesAsync(cmd.Lines.Select(l => (l.ItemId, l.Quantity)), ct);

        var now = clock.UtcNow;
        var order = SalesOrder.Create(await db.NextOrderNumberAsync(now, ct), customer.Id, new UserId(currentUser.UserId), now);
        order.SetLines(lines, currentUser.Role);

        // Early feedback only; Submit re-checks under a lock.
        customer.EnsureWithinCreditLimit(order.Total, await db.PendingTotalAsync(customer.Id, order.Id, ct));

        db.SalesOrders.Add(order);
        await db.SaveChangesAsync(ct);
        return order.Id.Value;
    }
}
```

Three things to notice:

- **The validator checks shape only**: positive ids, at least one line, a sensible quantity. Whether this
  *customer* may order, or whether 1.5 Widgets makes sense, are business rules, so they're the domain's job.
- **The handler has no business `if`.** It loads, it hands things to the domain, it saves. Every "no" comes
  from `EnsureNotBlocked`, `SetLines` or `EnsureWithinCreditLimit`.
- **`order.Id.Value` after `SaveChangesAsync`**: the database assigns the id, and EF writes it back into the
  object.

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Features/CreateSalesOrder/CreateSalesOrderEndpoint.cs
using Carter;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed record CreateSalesOrderRequest(long CustomerId, IReadOnlyList<OrderLineInput> Lines);

internal sealed class CreateSalesOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/sales-orders", async (CreateSalesOrderRequest body, ISender sender, CancellationToken ct) =>
        {
            var id = await sender.Send(new CreateSalesOrderCommand(body.CustomerId, body.Lines), ct);
            return Results.Created($"/api/sales-orders/{id}", new { id });
        })
        .RequireAuthorization(p => p.RequireRole(SalesRoles.Sellers));
}
```

You might wonder why there's a `CreateSalesOrderRequest` *and* a `CreateSalesOrderCommand` with the same
fields. The request is the **HTTP contract**, what callers send over the wire; the command is the **internal
message**. Today they match. The day you rename a field for the API, or add one the command shouldn't trust
from the caller, they can differ without either side breaking.

`RequireRole(SalesRoles.Sellers)` is the endpoint saying "these roles only, full stop". A role check that depends on the
data, like "only managers may order Controlled items", lives in the domain instead, as you saw in
`EnsureRoleAllowed`.

## 4. One handler for every error

The handler needs `ValidationException` from FluentValidation, so the host references it directly:

```bash
dotnet add src/Erp.Api package FluentValidation --version 12.1.1
```

```csharp
// src/Erp.Api/GlobalExceptionHandler.cs
using System.Diagnostics;
using Erp.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ValidationException = FluentValidation.ValidationException;

namespace Erp.Api;

// One place that maps exceptions to HTTP statuses, answering with RFC 9457 problem+json.
internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> log, IHostEnvironment env) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            ValidationException          => (StatusCodes.Status400BadRequest, "Validation failed"),
            // Malformed JSON, a missing body: the request never reached a handler.
            BadHttpRequestException bad  => (bad.StatusCode, "The request could not be read"),
            NotFoundException            => (StatusCodes.Status404NotFound, "Resource not found"),
            DomainException              => (StatusCodes.Status422UnprocessableEntity, "Business rule violated"),
            _                            => (StatusCodes.Status500InternalServerError, "Unexpected error")
        };

        if (status == StatusCodes.Status500InternalServerError)
            log.LogError(ex, "Unhandled exception");

        var errors = (ex as ValidationException)?.Errors.Select(e => e.ErrorMessage).ToArray();

        // Never leak a stack trace outside development.
        var detail = status switch
        {
            StatusCodes.Status500InternalServerError when env.IsDevelopment() => ex.ToString(),
            StatusCodes.Status500InternalServerError                          => "An unexpected error occurred.",
            _ when errors is not null                                         => string.Join(" ", errors),
            _                                                                 => ex.Message
        };

        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail, Instance = ctx.Request.Path };
        problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier;
        if (errors is not null)
            problem.Extensions["errors"] = errors;

        ctx.Response.StatusCode = status;
        // charset spelled out: some clients (Windows PowerShell 5.1) don't assume UTF-8, and ₱ arrives garbled.
        await ctx.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json; charset=utf-8", ct);
        return true;
    }
}
```

`IExceptionHandler` is ASP.NET Core's hook for exactly this. **`BadHttpRequestException`** is what
ASP.NET Core throws when it can't even read the request, say a body that isn't JSON; without its line, a
typo in a client's JSON would look like a server crash. The **`traceId`** in every error body is the
request's id; when a user reports a problem, they can quote it, and you can find that exact request in your
logs. Business messages (404, 422) are meant for people, so they're shown as they are. A validation failure
lists its messages twice: joined into one sentence in `detail`, for a person to read, and one by one in
`errors`, for a program to show next to each field. FluentValidation's own exception message is written for
developers, with field names and severities, so it never reaches the caller. A 500's details stay on the
server in production.

In `Program.cs`, register it and turn it on as the first thing in the pipeline, so it catches everything
after it:

```csharp
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
// ...
var app = builder.Build();
app.UseExceptionHandler();
```

## 5. Swagger: try your endpoints in the browser

From now on, there'll be more endpoints than you'll want to type `curl` for. **Swagger** reads your
endpoints and builds a page where you can call them. Swashbuckle (MIT) does it:

```bash
dotnet add src/Erp.Api package Swashbuckle.AspNetCore --version 10.2.3
```

```csharp
// in Program.cs, with the other services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Erp API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer",
        In = ParameterLocation.Header, Description = "The accessToken from POST /api/auth/login."
    });
    options.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", doc, null), new List<string>() }
    });
});

// after UseExceptionHandler
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
```

(Add `using Microsoft.OpenApi;` at the top.) The security definition adds an **Authorize** button: paste an
access token there once, and every call you make from the page carries it. (If you're already signed in to
the app in the same browser, the cookie works too.) Swagger only runs in
Development; a production server doesn't advertise its API to strangers.

## 6. The first screen

Every page will call the API, and every page must handle the answers the same way: a 422 becomes a message
the user can read, a 401 means the session ended. So all calls go through one class:

```csharp
// src/Erp.Web/Services/ApiClient.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MudBlazor;

namespace Erp.Web.Services;

// Every call to the API goes through here, so every page handles errors the same way: the API's problem details
// become a message on screen, and a 401 (session ended) sends the user back to sign in.
public sealed class ApiClient(HttpClient http, ISnackbar snackbar, CookieAuthenticationStateProvider auth)
{
    private sealed record Problem(string? Title, string? Detail, int? Status);
    private sealed record Created(long Id);

    public async Task<T?> Get<T>(string url)
    {
        var response = await http.GetAsync(url);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<T>() : await Fail<T>(response);
    }

    public Task<bool> Post(string url, object? body = null, string? success = null) => Send(HttpMethod.Post, url, body, success);
    public Task<bool> Put(string url, object body, string? success = null) => Send(HttpMethod.Put, url, body, success);

    // For "create" calls: the API answers 201 with { id }.
    public async Task<long?> Create(string url, object body, string? success = null)
    {
        var response = await http.PostAsJsonAsync(url, body);
        if (!response.IsSuccessStatusCode) return await Fail<long?>(response);
        if (success is not null) snackbar.Add(success, Severity.Success);
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private async Task<bool> Send(HttpMethod method, string url, object? body, string? success)
    {
        using var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
        var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return await Fail<bool>(response);
        if (success is not null) snackbar.Add(success, Severity.Success);
        return true;
    }

    private async Task<T?> Fail<T>(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            auth.Refresh();   // the session ended; the layout sends them to the sign-in page
            return default;
        }

        var message = response.StatusCode switch
        {
            HttpStatusCode.Forbidden => "You don't have permission to do that.",
            HttpStatusCode.TooManyRequests => "Too many requests. Wait a moment and try again.",
            _ => await ProblemText(response)
        };
        snackbar.Add(message, response.StatusCode == HttpStatusCode.Conflict ? Severity.Warning : Severity.Error);
        return default;
    }

    private static async Task<string> ProblemText(HttpResponseMessage response)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<Problem>();
            return problem?.Detail ?? problem?.Title ?? $"The server answered {(int)response.StatusCode}.";
        }
        catch (JsonException)
        {
            return $"The server answered {(int)response.StatusCode}.";
        }
    }
}
```

This is where the Problem Details from step 4 pay off. The API already wrote a sentence for people
("Customer 'Gamma Traders' is blocked and cannot place orders."), so the app just shows `detail` in a
**snackbar**, the small message that slides up at the bottom of the screen. The page itself never deals with
status codes: a call either returns what it asked for, or `null`/`false` after the user has been told why.

Register it in `Erp.Web/Program.cs`, after the `HttpClient`:

```csharp
builder.Services.AddScoped<ApiClient>();
```

and give the snackbar somewhere to appear, in `MainLayout.razor` under `<MudPopoverProvider />`:

```razor
<MudSnackbarProvider />
```

The page will be for sellers only, and that check needs the role names. The app must not reference
BuildingBlocks (that would pull Mediator and FluentValidation into the browser), so it compiles just the one
file. In `Erp.Web.csproj`:

```xml
<!-- Role names are defined once, in BuildingBlocks. The file is compiled in here too, not copied. -->
<ItemGroup>
  <Compile Include="..\Shared\Erp.Shared.BuildingBlocks\Abstractions\Roles.cs" Link="Shared\Roles.cs" />
</ItemGroup>
```

and `@using Erp.Shared.BuildingBlocks` in `_Imports.razor`. The names still live in exactly one place. That's
also why lesson 01 set `ReferenceOutputAssembly="false"` on the host's reference to the app: otherwise
`Roles` would reach `Erp.Api` twice, once from BuildingBlocks and once from `Erp.Web`, and the compiler would
refuse with `CS0433`.

Is the browser app allowed to hide things by role? Yes, as a courtesy. A hidden button isn't security: the
API checks every call on its own, and a rep who builds the URL by hand still gets a 403.

Now the page. We can't search customers or items yet (reading lists is next lesson), so for today the
form asks for ids. It's plain on purpose; [lesson 07](07-find-orders.md) gives it search boxes.

```razor
@* src/Erp.Web/Pages/Sales/OrderEditor.razor *@
@page "/sales-orders/new"
@attribute [Authorize(Roles = $"{Roles.SalesRep},{Roles.SalesManager},{Roles.Admin}")]
@inject ApiClient Api
@inject ISnackbar Snackbar

<PageTitle>New order · ERP</PageTitle>

<MudText Typo="Typo.h4" Class="mb-4">New sales order</MudText>

<MudPaper Class="pa-4 mb-4" Outlined="true">
    <MudNumericField T="long" Label="Customer id" @bind-Value="_customerId" Min="1" Style="max-width:200px" />
</MudPaper>

<MudPaper Class="pa-4 mb-4" Outlined="true">
    <MudText Typo="Typo.h6" GutterBottom="true">Lines</MudText>
    @foreach (var line in _lines)
    {
        <MudStack Row="true" AlignItems="AlignItems.Center" Spacing="3" Class="mb-2">
            <MudNumericField T="long" Label="Item id" @bind-Value="line.ItemId" Min="1" Style="max-width:160px" />
            <MudNumericField T="decimal" Label="Quantity" @bind-Value="line.Quantity" Min="0" Style="max-width:160px" />
            <MudIconButton Icon="@Icons.Material.Filled.Delete" OnClick="() => _lines.Remove(line)" aria-label="Remove line" />
        </MudStack>
    }
    <MudButton OnClick="() => _lines.Add(new Line())" StartIcon="@Icons.Material.Filled.Add" Variant="Variant.Text">Add line</MudButton>
</MudPaper>

<MudButton OnClick="Save" Variant="Variant.Filled" Color="Color.Primary" Disabled="!CanSave">Save draft</MudButton>

@code {
    private sealed class Line
    {
        public long ItemId { get; set; } = 1;
        public decimal Quantity { get; set; } = 1;
    }

    private long _customerId = 1;
    private List<Line> _lines = [new()];

    private bool CanSave => _customerId > 0 && _lines.Count > 0;

    private async Task Save()
    {
        var lines = _lines.Select(l => new { itemId = l.ItemId, quantity = l.Quantity }).ToList();
        if (await Api.Create("api/sales-orders", new { customerId = _customerId, lines }) is { } id)
        {
            _lines = [new()];
            Snackbar.Add($"Draft saved as order {id}.", Severity.Success);
        }
    }
}
```

- **Look at the two addresses.** `sales-orders/new` is a page of the app; `api/sales-orders` is the API. That's the `/api` prefix from lesson 01 earning its keep.
- **`[Authorize(Roles = ...)]`** hides the page from roles that can't use it. The API checks again on
  every call. The `$"{Roles.SalesRep},..."` string is built from the constants, so a role name is never
  typed by hand.
- **No `try`/`catch` anywhere.** If the API says no, `Create` returns `null`, and the user has already seen
  the reason.

Last, a way to get there. In `NavMenu.razor`, under the Home link:

```razor
<AuthorizeView Roles="@Sales">
    <MudNavGroup Title="Sales" Icon="@Icons.Material.Filled.PointOfSale" Expanded="true">
        <MudNavLink Href="sales-orders/new">New sales order</MudNavLink>
    </MudNavGroup>
</AuthorizeView>

@code {
    private static readonly string Sales = Join(Roles.SalesRep, Roles.SalesManager, Roles.Admin);

    private static string Join(params string[] roles) => string.Join(",", roles);
}
```

## How a request flows

```mermaid
sequenceDiagram
  participant C as Client
  participant E as CreateSalesOrderEndpoint
  participant V as ValidationBehavior
  participant H as CreateSalesOrderHandler
  participant D as Customer / ItemCategory / SalesOrder
  participant DB as PostgreSQL
  C->>E: POST /api/sales-orders (cookie or bearer token)
  E->>E: role check (SalesRep / SalesManager / Admin)
  E->>V: Send(CreateSalesOrderCommand)
  V->>H: valid shape → next()
  H->>DB: load customer, items + categories, next order number
  H->>D: EnsureNotBlocked, SetLines (sellable, role, unit, category cap), EnsureWithinCreditLimit
  H->>DB: SaveChangesAsync (order + lines, one transaction)
  H-->>C: 201 Created { id }
```

If any domain check throws, nothing is saved, and the exception handler turns the `DomainException` into a
422. Easy, right?

## Mistakes to avoid

- Putting "is this customer blocked?" in the validator. Validators check shape; the domain checks rules.
- Generating order numbers with `MAX(...) + 1`. Two people at once get the same number.
- Loading items one at a time inside the loop. That's one database trip per line; `Include` and `Contains`
  make it one.
- Catching errors on every page. `ApiClient` shows the API's own message once, for every page.
- Returning stack traces to the caller in production.
- Forgetting `UseExceptionHandler()` must come first. Anything registered before it isn't covered.

## Let's run it!

```bash
dotnet run --project src/Erp.Api
TOKEN=$(curl -s -X POST http://localhost:5264/api/auth/login -H "Content-Type: application/json" \
  -d '{"email":"rep@erp.local","password":"Password123!"}' | jq -r .accessToken)
```

A good order: two Widgets for Acme.

```bash
curl -s -X POST http://localhost:5264/api/sales-orders -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"customerId":1,"lines":[{"itemId":1,"quantity":2}]}'
# {"id":1}
```

Now each kind of "no":

| Try this body | You get | Because |
|---|---|---|
| `{"customerId":1,"lines":[]}` | **400** "An order needs at least one line." | The validator |
| `{"customerId":1,"lines":` (cut off) | **400** "The request could not be read" | ASP.NET Core couldn't read the JSON |
| `{"customerId":99,"lines":[{"itemId":1,"quantity":1}]}` | **404** "Customer 99 not found." | The handler |
| `{"customerId":3,"lines":[{"itemId":1,"quantity":1}]}` | **422** "Customer 'Gamma Traders' is blocked..." | `Customer.EnsureNotBlocked` |
| `{"customerId":2,"lines":[{"itemId":1,"quantity":100}]}` | **422** "...exceed the credit limit. Available credit: ₱500.00." | `Customer.EnsureWithinCreditLimit` |
| `{"customerId":1,"lines":[{"itemId":3,"quantity":1}]}` | **422** "Category 'Controlled' requires role 'SalesManager'." | `ItemCategory.EnsureRoleAllowed` |
| `{"customerId":1,"lines":[{"itemId":1,"quantity":1.5}]}` | **422** "Item GEN-001 is sold in whole pc..." | `SalesItem.EnsureQuantityFits` |
| `{"customerId":1,"lines":[{"itemId":5,"quantity":2.5}]}` | **201**: 2.5 kg of Copper Wire is fine | |

Each answer is a `application/problem+json` body with a `traceId`. Then open
`http://localhost:5264/swagger`, click **Authorize**, paste your token, and try the same calls from the
browser.

And now the way a sales rep will do it. Open `http://localhost:5264`, sign in as `rep@erp.local`, and click
**Sales → New sales order**. Save customer 1 with item 1, quantity 2: a green "Draft saved" appears. Change
the customer to 3 and save again: a red snackbar says Gamma Traders is blocked, word for word what the
domain wrote.

Check the order number in the database:

```bash
PGPASSWORD=erp_dev_only "/c/Program Files/PostgreSQL/18/bin/psql.exe" -h localhost -U postgres -d erp \
  -c "SELECT id, order_number, status FROM sales.sales_order;"
#  1 | SO-2026-000001 | 0
#  2 | SO-2026-000002 | 0
#  ...
```

## Let's check!

1. Which of these belongs in the validator, and which in the domain: "quantity is positive", "customer isn't
   blocked", "at most 5 Controlled items per order"?
2. Why does `MAX(order_number) + 1` go wrong, and how does a sequence avoid it?
3. Why is there a `CreateSalesOrderRequest` *and* a `CreateSalesOrderCommand`?
4. What status does each of these produce, and why: a blocked customer, an empty order, an unknown item?
5. Why does the error body include a `traceId`?
6. Why is Swagger only turned on in Development?

And that's where this lesson ends. You've built your first real feature, from a form in the browser all the
way to a saved order, with every rule in its proper place. Next time, we read orders back: a list to search,
a page for each order, and search boxes for that form. Good luck, developer!

---

← [Lesson 05 — Sign-in](05-sign-in.md) · Next: [Lesson 07 — Find orders](07-find-orders.md) →
