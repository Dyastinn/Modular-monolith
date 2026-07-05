# Phase 2 — Your Own Mediator vs. the Free One on GitHub

> 📎 Slow version of *what a mediator even is*:
> [beginner Phase 2](../plan-beginner-friendly/03-phase-2-mediator.md). Read it first if "post office"
> doesn't ring a bell. Here we make a real engineering **decision**: hand-roll it, or use the free
> library — and we wire up whichever you pick.

---

## 1. What we're building

The mediator that every command and query in this tutorial will flow through, plus the marker
interfaces (`ICommand`, `IQuery`) that make our slices readable. You'll end this phase able to send a
message and have the right handler run — the same `/health`-level proof, but for the mediator.

We present **two options** and you choose one. The rest of the tutorial's code works with *either*,
because they share the same interface names.

## 2. Why this is a real decision (not just ceremony)

The earlier plan hand-rolled a mediator to *teach the pattern*. For a production module you have a
genuine choice, and a senior should be able to argue it both ways. Here's the honest comparison:

| | **Hand-rolled (own)** | **Free library: `martinothamar/Mediator`** |
|---|---|---|
| What it is | ~150 lines you write and own | A NuGet package that generates the dispatch code at compile time |
| Cost | Free; your maintenance | Free (MIT — *verify the LICENSE yourself*); their maintenance |
| Speed | Reflection, cached per type — fine for normal loads | **Source-generated** → no reflection, very fast |
| Errors for a missing handler | **Runtime** (you find out when you call it) | **Compile-time** warning (found at build) |
| Learning value | High — you see exactly how it works | Lower — it's a black box |
| Risk | A bug is *your* bug | Depends on a third party; API caveats (see below) |
| Control | Total | You bend to their conventions |

> ℹ️ **Why not MediatR (the famous one)?** As the earlier plans noted, MediatR moved to a paid license
> for larger companies in 2025. It's still free under a revenue threshold, but this tutorial avoids it
> to keep things unambiguously free and to teach the pattern. `martinothamar/Mediator` is a genuinely
> free, MIT-licensed, source-generator alternative with a MediatR-shaped API.

**My recommendation for *this* tutorial:** **hand-roll it** (Option A). Not because it's better in
production, but because *understanding what's inside the box* is the entire point of a learning project,
and our load is tiny. Then in [Phase 13](13-wrap-up-checklist.md) we note exactly when you'd swap to the
library. If you'd rather use the library from day one, Option B is fully wired below and nothing else in
the tutorial changes.

## 3. The shared contracts (both options use these)

Whichever you pick, define these in `Erp.Shared.Messaging` — in the *shared kernel*, because every module
sends and handles through the same mediator. Our whole codebase depends on *these names*, not on the
implementation — that's what makes the two options interchangeable.

```csharp
// Marker: "I am a request that produces a TResponse."
public interface IRequest<out TResponse> { }

// Intent markers so the folder tree screams command-vs-query (they add no behavior):
public interface ICommand<out TResponse> : IRequest<TResponse> { }   // CHANGES state
public interface IQuery<out TResponse>   : IRequest<TResponse> { }   // READS state

// A tiny "nothing meaningful to return" type, for commands with no useful result.
public readonly record struct Unit
{
    public static readonly Unit Value = default;
}

// The worker for one request type.
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken ct);
}

// Handler markers that mirror ICommand/IQuery — they add no behavior, they just read better and let us
// constrain a future behavior to "commands only". Every slice in this tutorial declares its handler with
// one of these (e.g. `: ICommandHandler<SubmitSalesOrderCommand, Unit>`), NOT raw IRequestHandler.
public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse> { }
public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse> { }

// "The rest of the pipeline" — used by behaviors.
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

// A wrapper step around every handler (logging, validation, ...).
public interface IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct);
}

// The one thing callers use.
public interface ISender
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default);
}
```

> 📎 Line-by-line explanation of each of these interfaces is in
> [beginner Phase 2 §4a](../plan-beginner-friendly/03-phase-2-mediator.md). We won't repeat it — but
> note the two additions for this tutorial: `ICommand`/`IQuery` markers and the `Unit` type, both of
> which our real slices use constantly.

## 4. Why `ICommand` and `IQuery` markers exist

They carry **zero behavior** — so why bother? Because *intent should be visible at a glance*. When you
open `Sales/SalesOrders/` and see `SubmitSalesOrderCommand : ICommand<Unit>` next to
`GetSalesOrderQuery : IQuery<SalesOrderDto>`, you instantly know which one changes data and which only
reads — without opening the file. That single word also lets us apply behaviors selectively later (e.g.
"wrap every `ICommand` in a transaction," which we discuss in [Phase 11](11-cross-cutting.md)).

> ❓ **You might be wondering: "Isn't an interface with no methods pointless?"** A marker interface isn't
> about methods — it's a *label the compiler understands*. It lets us write `where T : ICommand<...>`
> and have the type system enforce "this only applies to commands." It's documentation that the
> compiler can act on. Cheap, and genuinely useful.

---

## Option A — Hand-roll the mediator (recommended for this tutorial)

### The dispatcher

Put this in `Erp.Shared.Messaging/Sender.cs`. It finds the handler for a request type, wraps it in the
registered behaviors, and runs it.

```csharp
using Microsoft.Extensions.DependencyInjection;

public sealed class Sender(IServiceProvider provider) : ISender
{
    // Cache one wrapper per concrete request type so we only reflect once per type.
    private static readonly ConcurrentDictionary<Type, RequestHandlerWrapper> _wrappers = new();

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
        var wrapper = _wrappers.GetOrAdd(
            request.GetType(),
            static requestType =>
            {
                var responseType = typeof(TResponse);
                var wrapperType = typeof(RequestHandlerWrapperImpl<,>)
                    .MakeGenericType(requestType, responseType);
                return (RequestHandlerWrapper)Activator.CreateInstance(wrapperType)!;
            });

        return ((RequestHandlerWrapperImpl<TResponse>)wrapper).Handle(request, provider, ct);
    }
}

// Non-generic base so we can store different closed types in one dictionary.
internal abstract class RequestHandlerWrapper { }

internal abstract class RequestHandlerWrapperImpl<TResponse> : RequestHandlerWrapper
{
    public abstract Task<TResponse> Handle(IRequest<TResponse> request, IServiceProvider sp, CancellationToken ct);
}

internal sealed class RequestHandlerWrapperImpl<TRequest, TResponse>
    : RequestHandlerWrapperImpl<TResponse>
    where TRequest : IRequest<TResponse>
{
    public override Task<TResponse> Handle(IRequest<TResponse> request, IServiceProvider sp, CancellationToken ct)
    {
        // 1) Resolve the one handler for this request type.
        var handler = sp.GetRequiredService<IRequestHandler<TRequest, TResponse>>();

        // 2) The innermost call is the handler itself.
        RequestHandlerDelegate<TResponse> pipeline = () => handler.Handle((TRequest)request, ct);

        // 3) Wrap it in behaviors, OUTERMOST LAST so the first-registered runs first.
        var behaviors = sp.GetServices<IPipelineBehavior<TRequest, TResponse>>().Reverse();
        foreach (var behavior in behaviors)
        {
            var next = pipeline;
            pipeline = () => behavior.Handle((TRequest)request, next, ct);
        }

        // 4) Run the whole nested chain.
        return pipeline();
    }
}
```

> 📎 If the `MakeGenericType` / `Activator` lines look like hieroglyphics — that's expected, and
> [beginner Phase 2 §4b](../plan-beginner-friendly/03-phase-2-mediator.md) gives you explicit permission
> to copy them and move on. What matters is steps 1–4 in the comments: **resolve handler → make it the
> innermost call → wrap in behaviors → run.** The "way in / way out" nesting is explained with a diagram
> in that same beginner phase.

### Registration — shared engine, per-module handlers

Registration splits along the module boundary, and that split is the whole point of a modular monolith.
The **shared** pieces (the `Sender` and the cross-cutting behaviors) register once in
`Erp.Shared.Infrastructure`:

```csharp
public static IServiceCollection AddMessaging(this IServiceCollection services)
{
    services.AddScoped<ISender, Sender>();
    // Behaviors run in registration order — Logging OUTERMOST, then Validation.
    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
    return services;
}
// AddSharedInfrastructure() calls AddMessaging().
```

Each **module** registers *its own* handlers and validators by scanning *its own assembly*, in its
`DependencyInjection.cs`:

```csharp
// Modules/Sales/DependencyInjection.cs  — a static class in the Erp.Modules.Sales assembly
public static class DependencyInjection
{
public static IServiceCollection AddSalesModule(this IServiceCollection services, IConfiguration config)
{
    // `typeof(DependencyInjection)` is just "some type that lives in THIS module's assembly" — any type
    // in the module works; we use the class this method is declared in. (No magic marker class needed.)
    var assembly = typeof(DependencyInjection).Assembly;   // the Erp.Modules.Sales assembly

    // Register every IRequestHandler<,> defined in THIS module.
    var handlerTypes = assembly.GetTypes()
        .Where(t => !t.IsAbstract && !t.IsInterface)
        .SelectMany(t => t.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
            .Select(i => new { Implementation = t, Service = i }));
    foreach (var h in handlerTypes) services.AddTransient(h.Service, h.Implementation);

    services.AddValidatorsFromAssembly(assembly);   // this module's FluentValidation validators
    // ... plus this module's repositories, TypeHandlers, IStockReservation impl (Phase 7), etc.
    return services;
}
}
```

> ⚠️ **`AddValidatorsFromAssembly` needs an extra package.** That helper lives in
> **`FluentValidation.DependencyInjectionExtensions`**, *not* the base `FluentValidation` package — so add it
> to the Sales module (`dotnet add src/Modules/Sales package FluentValidation.DependencyInjectionExtensions`).
> It's listed in [Phase 1 §5](01-solution-setup.md). Without it, `AddSalesModule` won't compile.

No module registers another's handlers — the DI wiring respects the module walls, exactly like the project
references do. `Program.cs` calls `AddSharedInfrastructure()` then each `Add*Module()`.

---

## Option B — Use the free `martinothamar/Mediator` library

If you prefer the library, you **do not write `Sender.cs` at all**. Instead:

1. Install the packages (from [Phase 1 §5](01-solution-setup.md)):
   ```bash
   dotnet add src/Shared/Erp.Shared.Messaging package Mediator.Abstractions
   dotnet add src/Erp.Api package Mediator.SourceGenerator
   ```
2. Its API is MediatR-shaped, so it already provides `IRequest`, `IRequestHandler`, `IPipelineBehavior`,
   and an `IMediator`/`ISender`. You can **delete our hand-rolled `IRequest`/`ISender`** and use theirs —
   *or* keep thin adapters. Simplest: use theirs directly and keep only our `ICommand`/`IQuery` markers
   extending *their* `IRequest`.
3. Register it once:
   ```csharp
   builder.Services.AddMediator();   // provided by the library; source-generates the dispatch
   ```

Your **call sites are identical** — every endpoint still just does `sender.Send(...)` — which is the
real payoff of coding to the interface. But be precise about what *does* change with Option B, so you're
not surprised:

- Handlers implement **the library's** `IRequestHandler<,>` and return **`ValueTask<T>`**, not `Task<T>`.
  (A one-word change per handler signature; the bodies are the same.)
- Pipeline behaviors match **the library's** `IPipelineBehavior` signature (also `ValueTask`-based), not
  the one we define in §3.
- You use **their** `IRequest`/`ISender`, keeping only our `ICommand`/`IQuery` markers (extending theirs).

So "swap the engine, keep the code" is true for your *endpoints and orchestration logic*, but the handler
and behavior **signatures** follow whichever engine you picked. Everything conceptual — the flow in §5, the
validator, the domain — is unchanged. Pick one engine and stay on it; don't mix.

> ⚠️ **Honest caveats of the library** (from its own docs — verify current state): performance can
> degrade past ~500 message types (being addressed), no generic requests/notifications, and handlers
> return `ValueTask` rather than `Task`. For a module this size, none of these bite. But a senior names
> the caveats before adopting a dependency — never adopt one you can't summarize the downsides of.

---

## 5. How a request flows through the mediator (either option)

Using our first real command as an example (built in Phase 6):

```
Endpoint: sender.Send(new CreateSalesOrderCommand(...))
   → Sender finds CreateSalesOrderHandler
   → wraps it: LoggingBehavior( ValidationBehavior( handler ) )
   → LoggingBehavior logs "Handling CreateSalesOrderCommand", starts timer     (way in)
       → ValidationBehavior runs the FluentValidation validator                (way in)
           → CreateSalesOrderHandler does the real work                        (the handler)
       ← ValidationBehavior returns (nothing after next)                       (way out)
   ← LoggingBehavior logs "Handled ... in 9ms"                                 (way out)
   → result (the new order id) returns to the endpoint
```

That "way in / way out" nesting is the one genuinely tricky idea; the U-shaped diagram in
[beginner Phase 2](../plan-beginner-friendly/03-phase-2-mediator.md) is worth re-reading if it's fuzzy.

## 6. Why each component exists

| Component | Why it exists |
|---|---|
| `ISender` | The single dependency an endpoint needs. Endpoints stay ignorant of handlers and their dependencies. |
| `IRequestHandler<,>` | One handler per use case; trivially unit-testable as a plain class. |
| `IPipelineBehavior<,>` | Cross-cutting concerns (logging, validation, later maybe transactions) written **once**, applied to all. |
| `Sender` (Option A) *or* the library (Option B) | The engine that resolves the handler and composes the behaviors. Interchangeable. |
| `ICommand`/`IQuery` | Visible intent + a hook for command-only behaviors. |
| `Unit` | Lets commands that change state but return nothing still fit the `IRequest<TResponse>` shape. |

## 7. Common mistakes & best practices

- ❌ **Forgetting to register a handler.** With the hand-rolled version you'll get a runtime
  "no service for `IRequestHandler<...>`" error. ✅ Each module's `Add*Module` scans its own assembly and
  registers its handlers automatically — so as long as your handler lives in that module, it's found.
- ❌ **Getting behavior order backwards** so validation logs nothing on failure. ✅ Register
  `LoggingBehavior` before `ValidationBehavior` (Logging outermost) — verify with the flow in §5.
- ❌ **Endpoints newing-up handlers directly** (`new CreateSalesOrderHandler(...)`). ✅ Always go through
  `sender.Send(...)`; that's the whole point.
- ✅ **Best practice: code to the interface, decide the engine later.** Because we only use `ISender` and
  `IRequestHandler`, choosing Option A vs B — or switching — is a *localized* change. That's exactly the
  benefit [beginner Phase 9](../plan-beginner-friendly/10-phase-9-modularity.md) promised.
- ✅ **Best practice: don't over-reach.** A mediator is worth it here because we have many use cases and
  real cross-cutting concerns. In a 3-endpoint app it would be ceremony — and knowing *that* is part of
  being senior.

---

## ✅ Check Yourself

1. Name two concrete differences between the hand-rolled mediator and the free library.
2. Why can the rest of the tutorial ignore which option you chose?
3. What do the `ICommand`/`IQuery` markers buy us if they contain no code?
4. In `LoggingBehavior( ValidationBehavior( handler ) )`, which runs first, and how is that controlled?

> 👉 Next: [Phase 3 — The database: schema, stored procedures, seed data](03-database-schema.md).
