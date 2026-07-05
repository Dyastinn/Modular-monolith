# Phase 2 — The Mediator (feel the pain first, then fix it)

> 📄 Expert version: [`../plan-Opus/03-phase-2-mediator.md`](../plan-Opus/03-phase-2-mediator.md)

**In one sentence:** We build a "post office" (the mediator) that finds the right handler for any request
— but first we deliberately feel the pain of *not* having one, so you understand *why* it exists.

**What you'll build:** The mediator contracts (`IRequest`, `IRequestHandler`, `ISender`,
`IPipelineBehavior`) in `Erp.Shared.Messaging`, a `Sender` that dispatches requests, and one
`LoggingBehavior`. After this, `/sales/ping` runs through the real mediator.

**Before you start:** Phase 1 skeleton compiles.

> This phase addresses a stated goal: *"I don't understand what problem the mediator solves because I've
> never felt the pain it fixes."* So we build the pain first. **Don't skim Step 1.**

---

## Step 1 — The pain: life without a mediator

Say you have an endpoint that creates an order. The handler that does the work needs several helpers
(repositories, a pricing service, etc.). Without a mediator, the **endpoint itself** has to know about
every one of those helpers and wire them together by hand:

```csharp
// (inside a Carter endpoint's AddRoutes)
app.MapPost("/sales-orders", async (
    CreateSalesOrderRequest req,
    ISalesOrderRepository orders,          // the handler needs this...
    ICustomerRepository customers,         // ...and this...
    IPricingService pricing,               // ...and this...
    IUnitOfWork uow,                       // ...and this...
    ILogger<CreateSalesOrderHandler> log,  // ...and this — all leaking up into the endpoint!
    CancellationToken ct) =>
{
    var handler = new CreateSalesOrderHandler(orders, customers, pricing, uow, log);
    return await handler.Handle(req, ct);
});
```

Look at how much the endpoint has to know: **every dependency of the handler.** The endpoint's job was
supposed to be "receive a web request and pass it on" — but now it's a wiring diagram.

Now imagine you want to add **logging, validation, timing, and a transaction** to *every* endpoint. You
copy-paste the same wrapper around all fifty of them. Fifty copies that slowly drift apart. Sound
familiar? It's the exact WebForms `Button_Click` sprawl from Phase 0 — just moved to a new location.

**The pain, named precisely:** the caller is *coupled* to how the handler is built, and cross-cutting code
(logging, etc.) is *duplicated everywhere*.

## Step 2 — The obvious-but-insufficient fix

You might think: "I'll make a factory, or a base class, to hide the wiring." That helps a little, but the
caller *still* has to name the specific handler, and *still* has to explicitly invoke the
logging/validation wrappers at each call site. You want the caller to say only **what** it wants done — not
**how** it's built or **what** wraps it.

## Step 3 — The fix: a mediator (post office)

The mediator flips it around. The endpoint now holds **one** thing — `ISender` — and just sends a message:

```csharp
// the entire Carter endpoint body:
app.MapPost("/sales-orders", async (CreateSalesOrderCommand cmd, ISender sender, CancellationToken ct) =>
    await sender.Send(cmd, ct));
```

When you call `sender.Send(cmd)`, three things happen:

1. It **finds** the handler registered for `CreateSalesOrderCommand`.
2. It **wraps** that handler in the registered pipeline behaviors (logging, validation, transaction) —
   automatically, for *every* message, defined once.
3. It **runs** it and returns the result.

The endpoint no longer knows the handler's dependencies or what wraps it. Want to add metrics to every
handler? Register one new behavior, *once*. Done.

> 💡 **Post office analogy:** You drop a letter in the mailbox (`Send`). You don't know the mail carrier's
> name, their route, or how sorting works. And the post office automatically weighs and stamps every
> package (the behaviors) — you don't do that per-letter.

## Step 4 — Building it (about 150 lines)

### 4a. The contracts — the "shapes" everyone agrees on

These live in **`Erp.Shared.Messaging`** — in the *shared kernel*, because **every module** sends and
handles requests through the same mediator. They're just interfaces (promises about method shapes):

```csharp
namespace Erp.Shared.Messaging;

// "I am a request that, when handled, produces a TResponse."
public interface IRequest<out TResponse> { }

// "I am the handler for a specific request type."
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken ct);
}

// A "next step in the chain" — used by behaviors below.
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

// A wrapper step that runs around every handler (logging, validation...).
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

Let's decode these — they're just *promises about shapes*:

- **`IRequest<TResponse>`** — "I'm a request, and handling me produces a `TResponse`." The `TResponse` is
  a fill-in-the-blank (see [Foundations §6](00a-FOUNDATIONS.md)): `IRequest<string>` produces a string,
  `IRequest<SalesOrderId>` produces a SalesOrderId. No methods — just a label that says "this can be sent."
- **`IRequestHandler<TRequest, TResponse>`** — "I know how to handle a specific request type and produce
  its answer." One method, `Handle`. Each request type gets exactly one handler.
- **`RequestHandlerDelegate<TResponse>`** — a `delegate` is just "a variable that holds a method you can
  call later." Think of it as *"the rest of the work, packaged up so a behavior can choose when to run
  it."* This is the `next()` you saw in the LoggingBehavior.
- **`IPipelineBehavior<TRequest, TResponse>`** — "I'm a wrapper step that runs around handlers." Its
  `Handle` receives the request *and* that `next` package, so it can do something, call `next()`, and do
  something after.
- **`ISender`** — the one thing callers touch. Single method `Send`. The mailbox slot of the post office.

> ℹ️ These names deliberately echo a popular library called **MediatR**. That way, if you ever read MediatR
> code elsewhere, it'll feel familiar. We build our own — it teaches the pattern far better and avoids any
> licensing questions. (We use libraries where they don't teach anything, like **Carter** and
> **FluentValidation**; we hand-roll the mediator precisely because building it *is* the lesson.)

> ❓ **"Why so many interfaces for something simple?"** Each interface is a *seam* — a clean place where two
> parts meet without knowing each other's insides. `ISender` lets the endpoint stay ignorant of handlers.
> `IRequestHandler` lets the mediator run any handler without knowing what it does. `IPipelineBehavior`
> lets you add logging/validation without touching a single handler. The interfaces *are* the decoupling.

### 4b. The dispatcher — the tricky part

Here's the hard bit: when someone calls `Send(request)`, the mediator only knows the request is *some*
`IRequest<TResponse>` — it doesn't know the exact type until runtime. It has to *find* the matching
`IRequestHandler` at runtime. This `Sender` lives in `Erp.Shared.Messaging` too.

There are two ways. **We build the first (recommended):**

- **Approach A — cache a small wrapper per request type.** The first time it sees a
  `CreateSalesOrderCommand`, it builds a tiny helper that knows how to resolve that command's handler and
  behaviors, and remembers it in a dictionary. Fast after the first call. (This is how MediatR does it.)
- **Approach B — use `dynamic`.** Shorter code, but errors hide until runtime and it's slightly slower.

Here's the whole thing — *all* of it, so it actually compiles if you type it in. It's longer than the
`Send` method alone because it needs two tiny helper classes underneath; don't worry, you write this once
and basically never touch it again:

```csharp
// needs: using System.Collections.Concurrent;  using Microsoft.Extensions.DependencyInjection;
public sealed class Sender(IServiceProvider provider) : ISender
{
    // A cache: one small "wrapper" per request type, so we only do the tricky reflection once per type.
    private static readonly ConcurrentDictionary<Type, RequestHandlerWrapper> _wrappers = new();

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
        var wrapper = _wrappers.GetOrAdd(
            request.GetType(),
            static requestType =>
            {
                var wrapperType = typeof(RequestHandlerWrapperImpl<,>)
                    .MakeGenericType(requestType, typeof(TResponse));
                return (RequestHandlerWrapper)Activator.CreateInstance(wrapperType)!;
            });

        return ((RequestHandlerWrapperImpl<TResponse>)wrapper).Handle(request, provider, ct);
    }
}

// Two small helper base classes so we can keep differently-typed wrappers in one dictionary.
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
        // 1) find the ONE handler registered for this request type
        var handler = sp.GetRequiredService<IRequestHandler<TRequest, TResponse>>();
        // 2) the innermost step is the handler itself
        RequestHandlerDelegate<TResponse> pipeline = () => handler.Handle((TRequest)request, ct);
        // 3) wrap it in the behaviors (reversed, so the FIRST-registered ends up OUTERMOST)
        foreach (var behavior in sp.GetServices<IPipelineBehavior<TRequest, TResponse>>().Reverse())
        {
            var next = pipeline;
            pipeline = () => behavior.Handle((TRequest)request, next, ct);
        }
        // 4) run the whole nested chain and return its result
        return pipeline();
    }
}
```

Don't panic if this looks dense — it's the *one* genuinely fiddly file in the project. You write it once
and rarely touch it. All it does is the four numbered steps in the comments: find the handler, make it the
innermost call, wrap it in the behaviors, run it.

In plain words:
- `_wrappers` is a **cache** — a memory of work we've already figured out. The first time a
  `CreateSalesOrderCommand` arrives, we work out how to handle its type and *remember* it; every time
  after, we reuse that. (`ConcurrentDictionary` = "a cache that's safe when many requests hit it at once.")
- `request.GetType()` asks "what *exact* kind of request is this?" at runtime.
- The `Activator.CreateInstance(...MakeGenericType...)` line builds the right typed wrapper for this
  request type. **You do not need to understand this line to use the mediator.** It's plumbing you write
  once — the post office's sorting machine picking a bin by address.

> ❓ **"I don't understand `MakeGenericType` and `Activator`. Am I doomed?"** Not slightly. This is advanced
> C# ("reflection") that even experienced devs look up every time. This is the *one* place where I'd say:
> **copy it, get it working, and move on.** Understanding *what it accomplishes* (find the handler for a
> request type, cheaply) is the part that matters, and you already do.

> ℹ️ **Honest note on speed:** reflection has a small cost. It's cached per type, so it's fine for normal
> apps. If it ever showed up as slow in a profiler, that's your cue to switch to a source-generated library
> like `martinothamar/Mediator`. Don't optimize before you have a reason.

### 4c. One behavior, to prove the pipeline works

This behavior lives in `Erp.Shared.Messaging` (or a `Behaviors/` folder beside it) — shared, because every
module wants logging:

```csharp
public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> log)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        log.LogInformation("Handling {Request}", name);
        var sw = Stopwatch.StartNew();
        var response = await next();      // ← run the next thing in the chain (eventually the handler)
        log.LogInformation("Handled {Request} in {Ms}ms", name, sw.ElapsedMilliseconds);
        return response;
    }
}
```

See the `await next()` in the middle? That's the behavior calling "the rest of the chain." Everything
before it runs on the way *in*; everything after runs on the way *out*. This is how wrappers nest.

### The "way in, way out" idea, drawn out

This nesting is the one genuinely mind-bending part, so let's slow *way* down. Imagine you registered two
behaviors — `LoggingBehavior` (outermost) and `ValidationBehavior` (inside it) — around a handler:

```
Logging: "Handling CreateSalesOrderCommand"          ← way IN (before next())
   └─► Validation: check the input is valid           ← way IN
          └─► HANDLER: actually create the order       ← the real work
       ◄─┘ Validation: (nothing after next here)       ← way OUT
Logging: "Handled CreateSalesOrderCommand in 12ms"     ← way OUT (after next())
```

Read it top-to-bottom then bottom-to-top, like a `U` shape. Each `await next()` is a behavior saying "okay,
run everything *inside* me now, and let me know when it's done." Code *before* `await next()` runs on the
way down; code *after* runs on the way back up. The handler is at the bottom of the U — the only thing that
does the real work.

> 💡 **Analogy: nested gift boxes (Russian dolls).** To reach the gift you open the outer box (Logging
> starts its timer), then the next box (Validation checks), then the gift (the Handler runs). Then you
> close the boxes in reverse (Validation finishes, then Logging stops its timer and logs the total).
> "Outermost" just means "the box you open first and close last."

> ❓ **"What if a behavior *doesn't* call `await next()`?"** Then the chain *stops there* — nothing inside
> it runs. That's a feature: the `ValidationBehavior` in Phase 4 does exactly this. If the input is
> invalid, it throws *instead of* calling `next()`, so the handler never runs.

### 4d. Registration — shared mediator, per-module handlers

Registration splits along the module boundary, and this split is the whole point of a modular monolith.

**In `Erp.Shared.Infrastructure`**, an `AddMessaging()` helper registers `ISender` → `Sender` and the
shared behaviors **in the order they should wrap**; `AddSharedInfrastructure()` calls it (and, from Phase 4
on, also registers the DB plumbing and `AddCarter()`):

```csharp
// Erp.Shared.Infrastructure/DependencyInjection.cs
using Carter;
using Erp.Shared.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Shared.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSharedInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddMessaging();
        services.AddCarter();                 // Carter's own services, so app.MapCarter() works
        // (Phase 4 adds the DB connection factory + unit of work here too)
        return services;
    }

    public static IServiceCollection AddMessaging(this IServiceCollection services)
    {
        services.AddScoped<ISender, Sender>();
        // Behaviors wrap in registration order — FIRST registered is OUTERMOST.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));    // outermost
        // (Phase 4 registers ValidationBehavior<,> here, just inside Logging)
        return services;
    }
}
```

`services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>))` registers an **open generic**
— one line covers `IPipelineBehavior<PingQuery,string>`, `IPipelineBehavior<CreateSalesOrderCommand,…>`, and
every future request type, so you never touch this again.

**In each module's `DependencyInjection.cs`**, `AddSalesModule()` scans *its own assembly* for the handlers
*it* defines and registers each one. Inventory later scans its own assembly; no module registers another's —
the registration respects the walls:

```csharp
// Modules/Sales/DependencyInjection.cs
using Erp.Shared.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Sales;

public static class DependencyInjection
{
    public static IServiceCollection AddSalesModule(this IServiceCollection services, IConfiguration config)
    {
        var assembly = typeof(DependencyInjection).Assembly;   // THIS module's assembly

        // Find every class that implements IRequestHandler<,> and register it against that interface.
        foreach (var type in assembly.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false }))
            foreach (var handlerInterface in type.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)))
                services.AddScoped(handlerInterface, type);

        // (Phase 4 also registers this module's validators, repositories, and Dapper type handlers here.)
        return services;
    }
}
```

> ❓ **"Why scan by reflection instead of registering each handler by hand?"** Because a new feature should
> be *one folder*, not "one folder plus a line you have to remember to add to a registration list." The scan
> means dropping in a `PingHandler` (or `CreateSalesOrderHandler`) is enough — it's found automatically. The
> cost is the missing-handler-is-a-runtime-error trade-off we named in Step 5.

**Order matters** — the first behavior registered is the *outermost* wrapper.

## Step 5 — When is a mediator the WRONG choice?

Patterns aren't free, and a good engineer knows when *not* to use one.

**It's great when:** you have many features across modules, plus cross-cutting concerns (logging,
validation, transactions) that would otherwise be copy-pasted. That's exactly this project.

**It's the wrong call when:**

- You have a tiny app with a handful of endpoints and no cross-cutting concerns. Then the mediator is just
  ceremony — inject the handler directly.
- Your team doesn't know the pattern and the indirection confuses more than it helps.
- You're talking *between separate programs* — that's messaging/queues, a totally different tool. Our
  mediator only works *inside one program*. (Even *between modules* in this monolith, we don't use the
  mediator to cross the wall casually — Phase 5 shows the disciplined way.)

**And be honest about its costs even when you do use it:**

- **Indirection.** "Go to definition" on `Send(cmd)` lands on the mediator, not your handler. You learn to
  navigate by request type instead.
- **Errors show up at runtime.** Forget to register a handler? You find out when you run it, not when you
  compile. (Source-generated mediators catch this at build time; ours doesn't.)
- **It's not magic decoupling.** The mediator gives you a *place* to put clean code — it doesn't make bad
  handlers good.

---

## ✅ Check Yourself

1. **Describe, concretely, the coupling and duplication pain you get *without* a mediator.**
2. **What does `Send(cmd)` do, in three steps?**
3. **Why does a missing handler fail at runtime here, and how would a source-generated mediator change
   that?**
4. **Where do the mediator contracts live, and where are a module's handlers registered — and why the
   split?**
5. **What does the *order* you register behaviors control?**

---

## 👨‍🏫 Answer Key

**1. Describe the coupling and duplication pain without a mediator.**
*Coupling:* the endpoint has to construct the handler *and* every one of its dependencies (repositories,
pricing, unit-of-work, logger). Its job was "receive a request and pass it on," but it's become a wiring
diagram. *Duplication:* cross-cutting concerns (logging, validation, transactions) get copy-pasted around
*every* handler call — fifty endpoints, fifty copies that drift. It's the WebForms `Button_Click` sprawl in
a new spot.

**2. What does `Send(cmd)` do, in three steps?**
1. **Finds** the handler registered for that command's type. 2. **Wraps** it in the registered pipeline
behaviors — automatically, for every message. 3. **Runs** it and returns the result. The caller just says
*what* it wants done.

**3. Why does a missing handler fail at runtime, and how would a source-generated mediator change that?**
Our mediator finds handlers by looking them up *while the program runs* (reflection + the DI container). A
forgotten registration has nothing to find, so it fails *when you run that request*. A **source-generated**
mediator writes the wiring at *compile time*, so a missing handler is caught as a build error — earlier and
safer. We accept the runtime cost because hand-rolling teaches the pattern.

**4. Where do the contracts live, and where are handlers registered — and why the split?**
The **contracts and `Sender`** live in `Erp.Shared.Messaging` (the shared kernel), because every module
uses one mediator. Each **module registers its own handlers** by scanning its own assembly in its
`DependencyInjection.cs`. The split exists so a module owns and registers only *its* handlers — the DI
wiring respects the module walls, just like the project references do.

**5. What does the order you register behaviors control?**
**Which behavior is the outermost wrapper** — the nesting/run order. The first registered wraps all the
others. That's why `Logging` is outermost (so it logs even requests that fail validation) and `Validation`
sits inside it. Order = nesting order.

> ✅ **You're ready for Phase 3 if:** you can explain the "pain" in your own words (coupling + duplication)
> and say the three steps of `Send`. If the `Sender.cs` reflection code still looks like hieroglyphics —
> fine. You write that file once. The *concept* (post office) is what matters.
