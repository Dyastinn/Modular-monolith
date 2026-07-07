# Phase 2 — Your Own Mediator vs. the Free One on GitHub

> Everything you need is here — we explain *what a mediator is*, weigh two ways to get one, and build
> whichever you pick.
>
> 📎 *Optional* deeper dive on the "post office" idea, with a U-shaped diagram:
> [beginner Phase 2](../plan-beginner-friendly/03-phase-2-mediator.md). Nice if the concept is brand new,
> but this phase stands on its own.

---

> ⏱️ **Time for this phase:** ~1 hr. One real decision (hand-roll vs. library) plus ~150 lines of mediator
> to type — or a few lines to wire the library. The concepts matter more than the length.

## 1. What we're building

A **mediator** — the single "post office" that every command and query in this tutorial hands its work to,
so an endpoint never needs to know which class actually does the job. We'll build the shared contracts
(`ICommand`, `IQuery`, `ISender`, …), pick and wire an implementation, and finish with a live proof: a
`GET /sales/ping` that travels *endpoint → mediator → handler* and returns `"pong"`. That's the whole
pipeline working end-to-end — the walking skeleton for every real feature that follows.

**You have two options for the implementation, and you pick one now:**

- **Option A — hand-roll it (recommended here):** write ~150 lines of your own mediator, so you see exactly
  how a request finds its handler. High learning value; it's *your* code.
- **Option B — use the free library `martinothamar/Mediator`:** a MIT-licensed, source-generated NuGet
  package with a MediatR-shaped API. Less to write; a compile-time-checked black box.

The rest of the tutorial's code works with **either**, because both expose the same interface names — §2
argues the trade-off, then you choose and wire that one.

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

> 📦 **Packages for this phase — add these now.** This is the phase that first needs a mediator, cross-cutting
> behaviors, validation, and web endpoints, so here's where those packages come in (per Phase 1's "install
> only when needed" rule). Each `dotnet add` also drops a `<PackageVersion>` line into
> `Directory.Packages.props`.
> ```bash
> # The mediator contracts + behaviors need DI, logging, and validation abstractions:
> dotnet add src/Shared/Erp.Shared.Messaging package Microsoft.Extensions.DependencyInjection.Abstractions
> dotnet add src/Shared/Erp.Shared.Messaging package Microsoft.Extensions.Logging.Abstractions
> dotnet add src/Shared/Erp.Shared.Messaging package FluentValidation
>
> # The shared kernel hosts the mediator + Carter wiring:
> dotnet add src/Shared/Erp.Shared.Infrastructure package Carter
> dotnet add src/Shared/Erp.Shared.Infrastructure package Microsoft.Extensions.DependencyInjection.Abstractions
>
> # The Sales module gets Carter (its endpoints), FluentValidation, and the assembly-scan helper:
> dotnet add src/Modules/Sales package Carter
> dotnet add src/Modules/Sales package FluentValidation
> dotnet add src/Modules/Sales package FluentValidation.DependencyInjectionExtensions   # AddValidatorsFromAssembly
> dotnet add src/Modules/Sales package Microsoft.Extensions.DependencyInjection.Abstractions
>
> # The host maps every module's Carter endpoints:
> dotnet add src/Erp.Api package Carter
> ```
> ⚠️ **Carter also needs the ASP.NET shared framework.** A feature's `XxxEndpoint.cs` is a Carter
> `ICarterModule` that lives *inside* the Sales module and compiles against ASP.NET routing types
> (`IEndpointRouteBuilder` and later `RequireAuthorization`). Those come from a **`FrameworkReference`**,
> which has no `dotnet add` — add it by hand to **both** `Erp.Modules.Sales.csproj` **and**
> `Erp.Shared.Infrastructure.csproj`:
> ```xml
> <ItemGroup>
>   <FrameworkReference Include="Microsoft.AspNetCore.App" />
> </ItemGroup>
> ```
> Without it, the endpoint files won't compile. (Endpoints stay in `Features/`, never in the pure `Domain/`
> — the domain must not see Carter or ASP.NET.)

Whichever option you pick, define these contracts in `Erp.Shared.Messaging` — in the *shared kernel*, because
every module sends and handles through the same mediator. Our whole codebase depends on *these names*, not on
the implementation — that's what makes the two options interchangeable.

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
The **shared** engine (the `Sender`) registers once in `Erp.Shared.Infrastructure`:

```csharp
public static IServiceCollection AddMessaging(this IServiceCollection services)
{
    services.AddScoped<ISender, Sender>();
    // Cross-cutting behaviors (logging, validation) plug into this same pipeline later, in Phase 11 —
    // we register them there, when we actually write them. The Sender already supports them today.
    return services;
}
// AddSharedInfrastructure() calls AddMessaging().
```

> 🔑 **The pipeline is built now; the behaviors come later.** Notice the `Sender` above already wraps a
> request in whatever `IPipelineBehavior`s are registered — that capability exists from this phase. We just
> don't register any *concrete* behavior yet, because we haven't needed one. Logging and validation arrive in
> [Phase 11](11-cross-cutting.md) and slot straight in — no change to the engine.

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
> **`FluentValidation.DependencyInjectionExtensions`**, *not* the base `FluentValidation` package — it's one
> of the installs in §3 above. Without it, `AddSalesModule` won't compile. (There are no validators to find
> yet; the call harmlessly registers nothing until our first feature adds one.)

No module registers another's handlers — the DI wiring respects the module walls, exactly like the project
references do. `Program.cs` calls `AddSharedInfrastructure()` then each `Add*Module()`.

---

## Option B — Use the free `martinothamar/Mediator` library

If you prefer the library, you **do not write `Sender.cs` at all**. Instead:

1. Install the library's packages (instead of the `FluentValidation`/pipeline pieces you'd use for Option A's
   behaviors — though you still keep `FluentValidation` itself for validators):
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

## 5. Wire the host, then prove it — your checkpoint ✅

The engine exists; now make a request actually travel through it. Two small wiring steps, then a
one-feature walking skeleton — the same "prove the pipe" move as Phase 1's `/health`, but through the
mediator.

### 1) Wire the mediator and the module into `Program.cs`

Building on Phase 1's minimal `Program.cs`, add the two composition lines and `MapCarter()`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSharedInfrastructure(builder.Configuration);  // the shared kernel — for now, just calls AddMessaging()
builder.Services.AddSalesModule(builder.Configuration);           // scans the Sales assembly for handlers (+ validators)

var app = builder.Build();

if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }

app.MapGet("/health", () => Results.Ok("healthy"));
app.MapCarter();   // discovers every feature's ICarterModule endpoint

app.Run();

public partial class Program { }
```

> `AddSharedInfrastructure` is the shared kernel's one entry point (`Erp.Shared.Infrastructure`); this phase
> it just calls `AddMessaging()`. It grows in later phases (Dapper in Phase 3, JWT in Phase 4). `AddSalesModule`
> is the module's `DependencyInjection.cs` from the registration section above.

### 2) The `Ping` slice — two files, the shape every feature copies

`src/Modules/Sales/Features/Ping/PingHandler.cs`:

```csharp
public sealed record PingRequest(string Name) : IRequest<string>;

// Demo only: this is the ONE handler that implements IRequestHandler directly. Every real slice from
// Phase 5 on uses ICommandHandler / IQueryHandler instead — the intent-revealing markers from §4.
public sealed class PingHandler : IRequestHandler<PingRequest, string>
{
    public Task<string> Handle(PingRequest request, CancellationToken ct)
        => Task.FromResult($"pong: {request.Name}");
}
```

`src/Modules/Sales/Features/Ping/PingEndpoint.cs`:

```csharp
public sealed class PingEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet("/sales/ping", async (string name, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new PingRequest(name), ct)));
}
```

### 3) Run and verify

```bash
dotnet run --project src/Erp.Api
curl "http://localhost:5xxx/sales/ping?name=world"     # → "pong: world"
```

If you get `pong: world`, **the mediator works** — the endpoint knew nothing but `ISender`, yet the request
found `PingHandler` on its own. That's your Phase 2 checkpoint: the post office is delivering mail. Every real
feature from here is just a richer `PingRequest`.

> 🧭 **From WebForms:** `/sales/ping` is the `[WebMethod]` you'd have called from jQuery — except the endpoint
> no longer holds the logic. It hands a message to the mediator, which routes it to the handler. Swap the
> engine (Option A ↔ B) and this call site doesn't change.

## 6. How a request flows through the mediator (either option)

The ping above flows straight through today, because we haven't added any behaviors yet. Once
[Phase 11](11-cross-cutting.md) adds logging and validation to the *same* pipeline, a real command — say
`CreateSalesOrderCommand` from Phase 6 — flows like this, nesting the behaviors around the handler:

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

That "way in / way out" nesting is the one genuinely tricky idea. If it's fuzzy, the
[beginner Phase 2](../plan-beginner-friendly/03-phase-2-mediator.md) walkthrough draws it as a U-shaped
diagram — an *optional* deeper look.

## 7. Why each component exists

| Component | Why it exists |
|---|---|
| `ISender` | The single dependency an endpoint needs. Endpoints stay ignorant of handlers and their dependencies. |
| `IRequestHandler<,>` | One handler per use case; trivially unit-testable as a plain class. |
| `IPipelineBehavior<,>` | Cross-cutting concerns (logging, validation, later maybe transactions) written **once**, applied to all. |
| `Sender` (Option A) *or* the library (Option B) | The engine that resolves the handler and composes the behaviors. Interchangeable. |
| `ICommand`/`IQuery` | Visible intent + a hook for command-only behaviors. |
| `Unit` | Lets commands that change state but return nothing still fit the `IRequest<TResponse>` shape. |

## 8. Common mistakes & best practices

- ❌ **Forgetting to register a handler.** With the hand-rolled version you'll get a runtime
  "no service for `IRequestHandler<...>`" error. ✅ Each module's `Add*Module` scans its own assembly and
  registers its handlers automatically — so as long as your handler lives in that module, it's found.
- ❌ **Getting behavior order backwards** so validation logs nothing on failure. ✅ When you add them in
  Phase 11, register `LoggingBehavior` before `ValidationBehavior` (Logging outermost) — verify with the flow
  in §6.
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
