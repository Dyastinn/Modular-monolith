# Phase 11 — Cross-Cutting Concerns: Validation, Error Handling, Logging

> Self-contained: this phase completes the pipeline the earlier phases were designed around — it's where the
> `ValidationBehavior`, `LoggingBehavior`, and global error handling you kept seeing "arrives in Phase 11"
> notes for actually get built and wired. Money is in **Philippine Pesos (₱)**.

---

> ⏱️ **Time for this phase:** ~1–1.5 hr. Two behaviors and one global exception handler, written **once** and
> applied to every endpoint. High leverage for the effort — this is the mediator paying you back.

## 1. What we're building

The wrappers that apply to *every* request, written once:

- A **ValidationBehavior** that runs FluentValidation before any handler.
- A **LoggingBehavior** that logs and times every request.
- A **global exception handler** that turns our domain exceptions into clean, consistent HTTP responses
  (400/403/404/409/422) instead of raw 500s with stack traces.

## 2. Why it's designed this way (the problem, before any code)

Before we write a line, be clear on *what problem* each of these three pieces solves — that's why we build
them at all:

- **The problem error handling solves.** Throughout this tutorial the handlers have thrown meaningful
  exceptions — `DomainException`, `NotFoundException`, `ConcurrencyException`, `ValidationException`. That
  was deliberate: a handler stays focused on the happy path and simply *throws* when a rule is broken,
  instead of cluttering itself with "and now build the right HTTP response." But a thrown exception, left
  alone, becomes an ugly **500 with a stack trace** — useless to the client and a security leak. So
  **something central** must catch each exception and translate it to the *right* status (a blocked customer
  is a `422`, not a `500`). Doing that in every endpoint would be the copy-paste sprawl the mediator exists
  to kill — so it lives in **one** global handler.
- **The problem validation-as-a-behavior solves.** We want every command's input checked *before* its
  handler runs, without each handler remembering to call its validator. A pipeline behavior does it for all
  of them, once.
- **The problem logging-as-a-behavior solves.** We want a timed record of every request (and every failure)
  without pasting `log.LogInformation` into every handler.

This is also the phase that **completes the pipeline** the whole course was built around: back in Phase 2
the mediator was designed to wrap handlers in behaviors, but we registered none yet; the feature phases threw
exceptions that nothing formatted. Now, having *seen* the need across real features, we build the machinery —
which is exactly when it makes sense, not before.

> 💡 This is the concrete answer to "what does the mediator/pipeline actually *buy* me?" — you write
> logging, validation, and error handling **once**, and every endpoint gets them. Add another endpoint and
> it's covered automatically. That leverage is the payoff of Phase 2.

### The behavior we deliberately don't write: a `TransactionBehavior`

Once you've seen how cheap a behavior is, an idea becomes almost irresistible: the `ICommand<>` marker from
[Phase 2](02-mediator-own-vs-free.md) makes it trivial to wrap **every command** in a database transaction.
One class, and no handler ever writes `BeginTransaction()` again:

```csharp
// The tempting version. We are NOT going to build this.
public sealed class TransactionBehavior<TRequest, TResponse>(IDbConnectionFactory factory)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : ICommand<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        using var conn = factory.Create();
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        try   { var result = await next(); tx.Commit(); return result; }
        catch { tx.Rollback(); throw; }
    }
}
```

Plenty of real codebases do exactly this. Here's why we don't, and it isn't dogma:

- **The handler still needs the transaction object.** Look at any write you've built: `orders.AddAsync(order,
  tx, ct)`, `stock.TryReserveAsync(..., tx, ct)`. Every repository call takes `tx`. A behavior that opens the
  transaction has to get it *back down* to the handler somehow — an ambient scope, a scoped service, an
  `AsyncLocal`. You've replaced one visible variable with invisible machinery, and now "which transaction am
  I on?" is a question you answer by reading framework code.
- **It transacts things that shouldn't be.** `LoginCommand` is an `ICommand`, and it opens a transaction to
  write one refresh-token row. Fine. But so is a command that only *reads* and returns a projection, and now
  it holds locks for nothing. The marker says "changes state," not "needs a transaction."
- **The boundary stops being a decision.** [Phase 7](07-submit-reserve-concurrency.md)'s submit and
  [Phase 6](06-create-order.md)'s create both wrap their writes — but they open the transaction at *different
  moments*, after different read-only validation. That placement is a real design choice each handler makes,
  and Phase 3b's rule ("validate first, then `BeginTransaction()`") is precisely a rule about *where* the
  boundary goes. A behavior forces one answer on every slice.

So this course keeps `BeginTransaction()` written out, by hand, in the handler that owns it — visible in the
same file as the writes it protects. **Logging and validation are genuinely uniform across every request;
transactions are not.** That's the whole test for whether something belongs in a behavior: *would every
request want exactly this, in exactly this place?* Two yeses and one no.

## 3. The ValidationBehavior

Cross-cutting behaviors are shared by *every* module, so they live in `Erp.Shared.Messaging`. Back in
[Phase 2](02-mediator-own-vs-free.md), `AddMessaging()` registered only the `ISender` and left a note that
the behaviors would join here — so now we write them and add the two registration lines to `AddMessaging`:

```csharp
// Erp.Shared.Infrastructure — extend the AddMessaging() stub from Phase 2:
public static IServiceCollection AddMessaging(this IServiceCollection services)
{
    services.AddScoped<ISender, Sender>();
    // NEW this phase — order matters: Logging OUTERMOST, then Validation (see §4).
    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
    return services;
}
```

Both behaviors go in one file, `src/Shared/Erp.Shared.Messaging/Behaviors/Behaviors.cs`:

```csharp
// src/Shared/Erp.Shared.Messaging/Behaviors/Behaviors.cs
using System.Diagnostics;                 // Stopwatch (for LoggingBehavior, below)
using FluentValidation;                   // IValidator, ValidationContext, ValidationException
using Microsoft.Extensions.Logging;       // ILogger<T>

namespace Erp.Shared.Messaging;

public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (validators.Any())
        {
            var context = new ValidationContext<TRequest>(request);
            var failures = validators
                .Select(v => v.Validate(context))
                .SelectMany(r => r.Errors)
                .Where(f => f is not null)
                .ToList();

            if (failures.Count != 0)
                throw new ValidationException(failures);   // stop here; the handler never runs
        }
        return await next();
    }
}
```

If there are validation errors, it throws *before* `next()` — so the handler never runs on bad input.
The `throw` is caught by our global handler (§5) and turned into a 400 with the field errors. Notice a
handler can now *assume* its input is well-formed — a real simplification.

## 4. The LoggingBehavior

```csharp
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> log)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        log.LogInformation("→ {Request}", name);
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await next();
            log.LogInformation("← {Request} in {Ms}ms", name, sw.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "✗ {Request} failed in {Ms}ms", name, sw.ElapsedMilliseconds);
            throw;   // rethrow — logging observes, it doesn't swallow
        }
    }
}
```

Registered **outermost** (before `ValidationBehavior`) so it logs even requests that fail validation —
the ordering lesson from [Phase 2](02-mediator-own-vs-free.md).

> ⚠️ **Best practice: never log secrets or full request bodies blindly.** We log the request *type name*
> (`CreateSalesOrderCommand`), not its contents — a login command would contain a password. If you ever
> log request data, scrub sensitive fields first. A logged password is a real breach.

## 5. Global exception handling → consistent HTTP responses

We map each exception type to an HTTP status, in one place, using ASP.NET Core's `IExceptionHandler`.
`Erp.Api/GlobalExceptionHandler.cs`:

```csharp
// src/Erp.Api/GlobalExceptionHandler.cs
using System.Security.Authentication;              // AuthenticationException
using Erp.Shared.BuildingBlocks;                   // DomainException
using Erp.Shared.Common;                           // NotFoundException, ForbiddenException, ConcurrencyException
using Microsoft.AspNetCore.Diagnostics;            // IExceptionHandler
using ValidationException = FluentValidation.ValidationException;   // disambiguate from System.ComponentModel's

namespace Erp.Api;

public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> log,
    IHostEnvironment env) : IExceptionHandler          // IHostEnvironment tells us Development vs Production
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            ValidationException            => (StatusCodes.Status400BadRequest,          "Validation failed"),
            NotFoundException              => (StatusCodes.Status404NotFound,            "Resource not found"),
            AuthenticationException        => (StatusCodes.Status401Unauthorized,        "Authentication failed"),
            ForbiddenException             => (StatusCodes.Status403Forbidden,           "Forbidden"),
            ConcurrencyException           => (StatusCodes.Status409Conflict,            "Conflict — please reload"),
            DomainException                => (StatusCodes.Status422UnprocessableEntity, "Business rule violated"),
            _                              => (StatusCodes.Status500InternalServerError, "Unexpected error")
        };

        // Always log full detail server-side, regardless of environment.
        if (status == StatusCodes.Status500InternalServerError)
            log.LogError(ex, "Unhandled exception");

        // The 500 "detail" is the ONLY environment-sensitive bit:
        //  • Development → show the real message + stack trace, so you can debug without digging in logs.
        //  • Production  → a bland message, so we never leak internals (table names, paths, stack traces).
        // Known business errors (422/404/409/…) are safe to show verbatim in BOTH environments — their
        // messages are meant for the user ("Customer is blocked").
        string? detail = status switch
        {
            StatusCodes.Status500InternalServerError when env.IsDevelopment() => ex.ToString(), // full stack trace
            StatusCodes.Status500InternalServerError                          => "An unexpected error occurred.",
            _                                                                 => ex.Message
        };

        var problem = new
        {
            title,
            status,
            detail,
            errors = ex is ValidationException v ? v.Errors.Select(e => e.ErrorMessage) : null
        };

        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(problem, ct);
        return true;   // we handled it
    }
}
```

Register it in `Program.cs`:

```csharp
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
// ...
app.UseExceptionHandler();   // early in the pipeline
```

> 🔍 **Why show the real error in Development but hide it in Production?** While *you're building*, an
> unexpected `500` is something you need to **debug** — the exception message and stack trace tell you which
> line blew up, and having them right there in the HTTP response (or Swagger) saves you round-trips to the
> log. But that exact same detail, shown to a *real user in production*, is a **security leak**: a stack
> trace can reveal table names, file paths, library versions, even connection details — a map an attacker
> would love. So we key it off `IHostEnvironment`: full detail in `Development`, a bland "An unexpected error
> occurred." in `Production` (with the full detail still written to the server log, where only operators can
> see it). ASP.NET Core knows which environment it's in from the `ASPNETCORE_ENVIRONMENT` variable
> (`Development` locally, `Production` when unset). **Same code, safe in both places** — you get debuggability
> where you need it and discretion where it matters.

> 🔑 **The status-code mapping *is* the design.** Each exception type carries business meaning, and the
> handler translates it to the HTTP language clients understand:
> - Broke a business rule (blocked customer, over credit) → **422 Unprocessable Entity**.
> - Malformed input → **400 Bad Request** (with field errors).
> - Not logged in / bad token → **401**; logged in but wrong role → **403**.
> - Someone else changed the row → **409 Conflict** (reload & retry).
> - Anything unexpected → **500**, and we *log the detail but hide it* from the client.

> ❓ **You might be wondering: "Why hide the detail on a 500 but show it on a 422?"** A 422 is a *known*
> business outcome — "Customer is blocked" is safe and *helpful* to show. A 500 is an *unexpected* bug;
> its message might leak a table name, a file path, or a stack trace an attacker could use. So we log the
> full 500 detail where only we can see it (server logs) and return a bland message to the client.
> Security people call this "don't leak internals." Make it a habit.

## 6. Defining the exception types

These are tiny; put the generic ones (`NotFoundException`, `ForbiddenException`, `ConcurrencyException`)
in `Erp.Shared.Common` so every module and the host can throw and catch them, and `DomainException` in
`Erp.Shared.BuildingBlocks` (it's part of the domain vocabulary every module reuses):

```csharp
// src/Shared/Erp.Shared.BuildingBlocks/BuildingBlocks.cs — domain vocabulary (you wrote this in Phase 1 §4b)
public class DomainException(string message) : Exception(message);        // "a business rule said no"
```

```csharp
// src/Shared/Erp.Shared.Common/Exceptions.cs — the transport-shaped ones the global handler maps
namespace Erp.Shared.Common;

public class NotFoundException(string message) : Exception(message);      // "that row doesn't exist"
public class ForbiddenException(string message) : Exception(message);     // "you're known, but not allowed"
public class ConcurrencyException(string message) : Exception(message);   // "someone changed it under you"

// ValidationException comes from FluentValidation; AuthenticationException from System.Security.Authentication.
```

> 🔑 **Why `DomainException` lives somewhere different from the other three.** `DomainException` is a *domain*
> word — a `SalesOrder` throws it, and the pure domain may only reference `BuildingBlocks`. The other three
> describe the shape of a *failed request* ("not found," "forbidden," "conflict"), which is a
> handler-and-host concern, so they sit in `Common` where the host can reach them too. Put
> `NotFoundException` in `BuildingBlocks` and you've quietly told the domain that HTTP exists.

## 7. Registering everything (the composition root, filled in)

By now `Program.cs` composes the shared kernel and each module — registration follows the module walls:

```csharp
// AddSharedInfrastructure (Erp.Shared.Infrastructure) — the platform every module uses:
services.AddMessaging();   // ISender + the LoggingBehavior/ValidationBehavior we added to it in §3 above
services.AddScoped<IDbConnectionFactory>(_ => new SqlConnectionFactory(config.GetConnectionString("ErpDb")!));  // Phase 3b
services.AddHttpContextAccessor();
services.AddScoped<ICurrentUser, CurrentUser>();
services.AddSingleton<IClock, SystemClock>();                   // UtcNow wrapper — testable time (Phase 4 §10)
services.AddSingleton<IPasswordHasher, PasswordHasher>();
services.AddScoped<IJwtTokenService, JwtTokenService>();
services.AddSingleton<IRefreshTokenService, RefreshTokenService>();  // refresh token generate/hash/expiry (Phase 4 §10)
services.AddScoped<IUserRepository, UserRepository>();          // Login/refresh (host/platform concern)
services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();   // shared: any module's aggregate (Phase 8 §6)

// The auth slices live in the HOST assembly (Erp.Api/Auth), NOT a module — and the handler scan in
// AddSalesModule only scans the Sales assembly. So register the host's own handlers + validators explicitly,
// or sender.Send(new LoginCommand(...)) fails at runtime with "no service for IRequestHandler<LoginCommand,…>".
services.AddScoped<IRequestHandler<LoginCommand, LoginResult>, LoginHandler>();
services.AddScoped<IValidator<LoginCommand>, LoginValidator>();
services.AddScoped<IRequestHandler<RefreshCommand, LoginResult>, RefreshHandler>();          // Phase 4 §10
services.AddScoped<IRequestHandler<LogoutCommand, Unit>, LogoutHandler>();
services.AddScoped<IRequestHandler<LogoutAllCommand, Unit>, LogoutAllHandler>();
services.AddScoped<IRequestHandler<ChangePasswordCommand, Unit>, ChangePasswordHandler>();   // Phase 4 Part 4
services.AddScoped<IValidator<ChangePasswordCommand>, ChangePasswordValidator>();

// AddSalesModule (Erp.Modules.Sales) — scans this assembly, registers ITS pieces:
services.AddValidatorsFromAssembly(salesAssembly);
// + IRequestHandler<,> scanning and IDomainEventHandler<> scanning (Phase 2 §5, Phase 8 §6)
services.AddScoped<ISalesOrderRepository, SalesOrderRepository>();   // Phase 6 §6
services.AddScoped<ICustomerRepository, CustomerRepository>();
services.AddScoped<IItemRepository, ItemRepository>();

// AddInventoryModule (Erp.Modules.Inventory):
services.AddScoped<IStockReservation, StockReservationService>();   // the contract Sales calls (Phase 7)
```

Read that list top to bottom and you can see the module walls in it: the **shared** block registers things
every module may use, each **module** block registers only its own, and `IStockReservation` is the single
place the two modules meet. Nothing in `AddSalesModule` knows the word `Inventory`.

## 8. How error handling changes a request flow

Recall Beta Industries ordering 100 Widgets ([Phase 6](06-create-order.md)) — it broke the credit limit:

```
... handler calls customer.EnsureWithinCreditLimit(₱2,500)
    → throws DomainException("Order total ... would exceed the credit limit ...")
→ LoggingBehavior catches, logs "✗ CreateSalesOrderCommand failed", rethrows
→ GlobalExceptionHandler maps DomainException → 422 Unprocessable Entity
→ client receives: { "title": "Business rule violated", "status": 422,
                     "detail": "Order total ₱2,500.00 would exceed the credit limit. Available credit: ₱500.00." }
```

Clean, consistent, informative — and the salesperson sees exactly why. No stack trace, no 500, no
special code in the endpoint. That's the cross-cutting machinery doing its job.

## 8b. Checkpoint — errors are now clean and consistent ✅

This is the phase that makes every earlier feature's *unhappy* path tidy. Re-run the rejections you saw
earlier and confirm they now come back as proper HTTP problems, not raw 500s:

```bash
AUTH=(-H "X-Api-Key: $KEY" -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json")

# Business rule → 422 (Phase 6's over-limit order): Beta ordering 100 Widgets
curl -X POST "$BASE/sales-orders" "${AUTH[@]}" \
     -d '{"customerId":2,"lines":[{"itemId":1,"quantity":100}]}'
# → 422 { "title": "Business rule violated", "detail": "Order total ₱2,500.00 would exceed..." }

# Malformed input → 400 with field errors (empty lines)
curl -X POST "$BASE/sales-orders" "${AUTH[@]}" -d '{"customerId":2,"lines":[]}'
# → 400 { "title": "Validation failed", "errors": ["An order needs at least one line."] }
```

Then confirm the dev-vs-prod switch: run with `ASPNETCORE_ENVIRONMENT=Development` and force an unexpected
error — the `500` response includes the stack trace. Run again with `ASPNETCORE_ENVIRONMENT=Production` and
the same error returns only `"An unexpected error occurred."` (the detail is in the server log). If business
errors return `422`, bad input returns `400` with field messages, and `500`s reveal detail only in
Development, **Phase 11 is done and verifiable** — the app now behaves professionally on every path.

## 9. Why each component exists

| Component | Why |
|---|---|
| `ValidationBehavior` | Runs input validation for every command in one place; handlers assume clean input. |
| `LoggingBehavior` | Observability for every request; times and records failures. |
| `GlobalExceptionHandler` | One place that maps exception types → HTTP statuses; consistent client errors. |
| Custom exception types | Let handlers throw *meaning* (not-found vs. rule-broken vs. conflict). |
| `AddSharedInfrastructure`/`Add*Module` | Keep the composition root readable; the shared kernel and each module register themselves. |

## 10. Common mistakes & best practices

- ❌ **`try/catch` in every endpoint.** ✅ One global handler; endpoints stay one-liners.
- ❌ **Returning 500 for business rule failures.** ✅ 422 (rule) / 400 (input) / 409 (conflict) — the
  status *means* something to the client.
- ❌ **Leaking stack traces / internal messages on 500.** ✅ Log server-side, return a bland message.
- ❌ **Logging passwords / full bodies.** ✅ Log type names; scrub sensitive fields.
- ❌ **Validation logic inside handlers.** ✅ FluentValidation via the behavior; handlers assume valid
  input.
- ✅ **Best practice: exceptions carry business meaning; the boundary maps them to transport.** The
  domain never mentions HTTP; the API layer owns the translation.

---

## ✅ Check Yourself

1. What does putting logging/validation/error-handling in behaviors + one handler buy you vs. per
   endpoint?
2. Why map `DomainException` to 422 but a random `Exception` to 500 — and why hide the 500's detail?
3. Why is `LoggingBehavior` registered before `ValidationBehavior`?
4. Why can a handler safely assume its input is already valid?

> 👉 Next: [Phase 12 — Testing the business rules](12-testing.md).
