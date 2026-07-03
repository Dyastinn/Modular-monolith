# Phase 11 — Cross-Cutting Concerns: Validation, Error Handling, Logging

> 📎 Builds directly on the pipeline behaviors from
> [beginner Phase 2](../plan-beginner-friendly/03-phase-2-mediator.md) and the validation split from
> [beginner Phase 4](../plan-beginner-friendly/05-phase-4-create-order.md). This is where we make the app
> behave *professionally* — consistent errors, useful logs, no leaked stack traces.

---

## 1. What we're building

The wrappers that apply to *every* request, written once:

- A **ValidationBehavior** that runs FluentValidation before any handler.
- A **LoggingBehavior** that logs and times every request.
- A **global exception handler** that turns our domain exceptions into clean, consistent HTTP responses
  (400/403/404/409/422) instead of raw 500s with stack traces.

## 2. Why it's designed this way

Throughout this tutorial the handlers have thrown meaningful exceptions — `DomainException`,
`NotFoundException`, `ConcurrencyException`, `ValidationException`. That was deliberate: handlers stay
focused on the happy path and *throw* when a rule is broken. **Something central** must catch those and
translate them to the right HTTP status. Doing that in every endpoint would be the copy-paste sprawl the
mediator exists to kill. So it lives in one place.

> 💡 This is the concrete answer to "what does the mediator/pipeline actually *buy* me?" — you write
> logging, validation, and error handling **once**, and all 8 endpoints get them. Add a 9th endpoint and
> it's covered automatically. That leverage is the payoff of Phase 2.

## 3. The ValidationBehavior

Cross-cutting behaviors are shared by *every* module, so they live in `Erp.Shared.Messaging` (registered
once by `AddSharedInfrastructure` → `AddMessaging`). `Erp.Shared.Messaging/Behaviors/ValidationBehavior.cs`:

```csharp
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
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> log) : IExceptionHandler
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

        // Log full detail server-side; return a clean message client-side.
        if (status == StatusCodes.Status500InternalServerError)
            log.LogError(ex, "Unhandled exception");

        var problem = new
        {
            title,
            status,
            detail = status == 500 ? "An unexpected error occurred." : ex.Message,   // don't leak internals on 500
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
public class DomainException(string message) : Exception(message);          // Domain
public class NotFoundException(string message) : Exception(message);        // Application
public class ForbiddenException(string message) : Exception(message);       // Application
public class ConcurrencyException(string message) : Exception(message);     // Application
// ValidationException comes from FluentValidation; AuthenticationException from System.Security.Authentication
```

## 7. Registering everything (the composition root, filled in)

By now `Program.cs` composes the shared kernel and each module — registration follows the module walls:

```csharp
// AddSharedInfrastructure (Erp.Shared.Infrastructure) — the platform every module uses:
services.AddScoped<ISender, Sender>();
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));    // outermost
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>)); // inner
services.AddScoped<IDbConnectionFactory>(_ => new SqlConnectionFactory(config.GetConnectionString("ErpDb")!));
services.AddScoped<IUnitOfWork, UnitOfWork>();
services.AddHttpContextAccessor();
services.AddScoped<ICurrentUser, CurrentUser>();
services.AddSingleton<IPasswordHasher, PasswordHasher>();
services.AddScoped<IJwtTokenService, JwtTokenService>();
services.AddScoped<IUserRepository, UserRepository>();          // Login (host/platform concern)

// AddSalesModule (Erp.Modules.Sales) — scans this assembly, registers ITS pieces:
services.AddValidatorsFromAssembly(salesAssembly);
// + handler scanning (Phase 2)
services.AddScoped<ISalesOrderRepository, SalesOrderRepository>();
services.AddScoped<ICustomerRepository, CustomerRepository>();
services.AddScoped<IItemRepository, ItemRepository>();
services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
// + register this module's Dapper TypeHandlers at startup

// AddInventoryModule (Erp.Modules.Inventory):
services.AddScoped<IStockReservation, StockReservationService>();   // the contract Sales calls (Phase 7)
```

## 8. How error handling changes a request flow

Recall Beta Industries ordering 100 Widgets ([Phase 6](06-create-order.md)) — it broke the credit limit:

```
... handler calls customer.EnsureWithinCreditLimit($2,500)
    → throws DomainException("Order total ... would exceed the credit limit ...")
→ LoggingBehavior catches, logs "✗ CreateSalesOrderCommand failed", rethrows
→ GlobalExceptionHandler maps DomainException → 422 Unprocessable Entity
→ client receives: { "title": "Business rule violated", "status": 422,
                     "detail": "Order total $2,500.00 would exceed the credit limit. Available credit: $500.00." }
```

Clean, consistent, informative — and the salesperson sees exactly why. No stack trace, no 500, no
special code in the endpoint. That's the cross-cutting machinery doing its job.

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
