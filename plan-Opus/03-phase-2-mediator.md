# Phase 2 — The Mediator (hand-rolled, motivated by pain)

**Goal:** understand what the mediator pattern buys you and what it costs, by first feeling the pain
of not having one, then building a ~150-line free implementation with MediatR-shaped interfaces.
**Produces:** `IRequest`, `IRequestHandler`, `ISender`, `IPipelineBehavior`, a `Sender` implementation,
DI registration, and one `LoggingBehavior`. The `Ping` slice now runs through it.
**Prereq:** Phase 1 skeleton compiles.

> This is one of your stated top goals: *"I do not yet understand what problem it solves because I have
> never experienced the pain it addresses."* So we build the pain first. Do not skim section 1.

---

## Step 1 — The problem: without a mediator

Imagine three endpoints, each needing a handler. Without a mediator, the endpoint (or a controller)
must know the concrete handler and all of its dependencies, and wire them by hand:

```csharp
// The endpoint has to construct/inject the exact handler and its dependencies.
app.MapPost("/orders", async (
    CreateOrderRequest req,
    IOrderRepository orders,          // dependency of the handler...
    ICustomerRepository customers,    // ...leaking up into the endpoint
    IPricingService pricing,
    IUnitOfWork uow,
    ILogger<CreateOrderHandler> log,
    CancellationToken ct) =>
{
    var handler = new CreateOrderHandler(orders, customers, pricing, uow, log);
    return await handler.Handle(req, ct);
});
```

Now add cross-cutting concerns — logging, validation, timing, a transaction — to **every** endpoint.
You copy-paste the same `try/catch`, the same "validate then run," the same "begin/commit" around
every handler call. Fifty endpoints later:

- The endpoints know the internals of every handler (their dependency lists).
- Cross-cutting code is duplicated fifty times and drifts.
- Adding a new endpoint means re-wiring all of it again.

This is exactly the WebForms `Button_Click` sprawl re-emerging, just relocated. **The pain is
coupling between the caller and the handler's construction, plus duplicated cross-cutting code.**

## Step 2 — The naive fix, and why it's not enough

You might extract a `HandlerFactory` or push everything into a base controller. That reduces some
duplication but the caller still names the handler, and cross-cutting logic still has to be invoked
explicitly at each call site. You want the caller to say only *what* it wants done, not *how* it's
wired or *what wraps it*.

## Step 3 — With a mediator

The mediator inverts it. The endpoint holds **one** dependency (`ISender`) and sends a message:

```csharp
app.MapPost("/orders", async (CreateOrderCommand cmd, ISender sender, CancellationToken ct) =>
    await sender.Send(cmd, ct));
```

`sender.Send(cmd)`:
1. Looks up the handler registered for `CreateOrderCommand`.
2. Wraps it in the registered **pipeline behaviors** (logging, validation, transaction) — once,
   centrally, for every message.
3. Runs it and returns the result.

The endpoint no longer knows the handler's dependencies or the cross-cutting stack. New behavior
(say, adding metrics) is registered **once** and applies to every handler.

## Step 4 — The implementation (hand-rolled, ~150 lines)

### 4a. The contracts (`Erp.Application/Abstractions/Messaging`)

```csharp
public interface IRequest<out TResponse> { }

public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken ct);
}

public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

public interface IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct);
}

public interface ISender
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default);
}
```

These names deliberately mirror MediatR so the mental model transfers if you ever read MediatR code.
We add thin `ICommand`/`IQuery` marker interfaces later (Phase 4/6) that extend `IRequest<T>`, purely
to make command-vs-query intent obvious in the folder tree.

### 4b. The dispatcher (`Erp.Infrastructure/Messaging/Sender.cs`)

The hard part: `Send<TResponse>` receives an `IRequest<TResponse>` whose concrete type is only known
at runtime, and must find `IRequestHandler<TThatConcreteType, TResponse>`. Two standard approaches —
**teach both, ship the first:**

**Approach A — generic wrapper cached per request type (recommended).** For each concrete request
type, build (once) a small non-generic wrapper that knows how to resolve the typed handler and
behaviors, and cache it in a `ConcurrentDictionary`. This is how MediatR does it. It confines
reflection to first-use per type.

**Approach B — `dynamic` dispatch.** Cast to `dynamic` and let the runtime binder pick the typed
`Send`. Shorter, but hides errors until runtime and has a small per-call cost.

Sketch of Approach A's core:

```csharp
public sealed class Sender(IServiceProvider provider) : ISender
{
    private static readonly ConcurrentDictionary<Type, RequestHandlerBase> _wrappers = new();

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
        var wrapper = (RequestHandlerWrapper<TResponse>)_wrappers.GetOrAdd(
            request.GetType(),
            static t => (RequestHandlerBase)Activator.CreateInstance(
                typeof(RequestHandlerWrapperImpl<,>).MakeGenericType(t, typeof(TResponse)))!);

        return wrapper.Handle(request, provider, ct);
    }
}
```

`RequestHandlerWrapperImpl<TRequest,TResponse>` resolves `IRequestHandler<TRequest,TResponse>` and all
`IPipelineBehavior<TRequest,TResponse>` from the provider, and composes the behaviors around the
handler by folding them into a `RequestHandlerDelegate` chain (behaviors run outermost-first).

> **Honesty about the reflection cost:** this is *exactly* why `martinothamar/Mediator` uses source
> generators — it moves this dispatch to compile time. Our reflection version is fine for a learning
> project and typical app loads (dispatch is cached per type). If dispatch ever shows up in a profile,
> that's your cue to switch to the source-generated library. Note it; don't pre-optimize.

### 4c. One behavior, to prove the pipeline

```csharp
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> log)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        log.LogInformation("Handling {Request}", name);
        var sw = Stopwatch.StartNew();
        var response = await next();
        log.LogInformation("Handled {Request} in {Ms}ms", name, sw.ElapsedMilliseconds);
        return response;
    }
}
```

### 4d. Registration

Scan the `Erp.Application` assembly for `IRequestHandler<,>` implementations and register each;
register `ISender` → `Sender`; register open-generic behaviors in the order they should wrap
(`LoggingBehavior<,>` now; `ValidationBehavior<,>` in Phase 4). Behavior order matters — the first
registered is the outermost wrapper.

## Step 5 — Benefits, tradeoffs, and when NOT to use it

**Benefits**
- Callers depend on one abstraction (`ISender`), not on handler internals.
- Cross-cutting concerns are written once as behaviors and apply uniformly.
- New use case = new request + handler; no wiring at the call site.
- Handlers are trivially unit-testable (plain classes, no HTTP).

**Tradeoffs (state these plainly)**
- **Indirection.** "Go to definition" on `Send(cmd)` lands on the mediator, not the handler. You
  navigate by request type, not by call graph. Some teams find this obscures flow.
- **Runtime resolution.** A missing handler registration fails at runtime, not compile time
  (source-generated mediators mitigate this with build warnings).
- **It's not free architecture.** A mediator does not *give* you decoupling; it gives you a *place*
  to put it. Bad handlers behind a mediator are still bad.

**When NOT to use it**
- A small app with a handful of endpoints and no cross-cutting concerns: a mediator is ceremony.
  Inject the handler/service directly.
- When the indirection costs more comprehension than the decoupling saves — e.g. a team unfamiliar
  with the pattern maintaining a tiny service.
- For request/response *between processes* — that's messaging/queues, a different tool. This mediator
  is strictly in-process.

> We use it here because the project is deliberately large enough (twelve operations, validation,
> transactions, logging) that the cross-cutting payoff is real. In a three-endpoint CRUD app it
> wouldn't be worth it — and saying so is part of the lesson.

## What Sonnet should produce

- The five contracts in `Application/Abstractions/Messaging`.
- `Sender` (Approach A) + wrapper types + DI registration in `Infrastructure/Messaging`.
- `LoggingBehavior<,>`, registered.
- `Ping` slice now resolves and runs through the real `Sender`; `GET /orders/ping` returns "pong"
  and logs the handling.
- A brief comment in `Sender.cs` noting the reflection-vs-source-generator tradeoff and the library
  alternative.

**NOT in this phase:** validation behavior, transactions, domain, DB. Just the mediator machinery.

---

## Checkpoint — you understand this phase when you can answer:

1. Describe the coupling/duplication pain that exists *without* a mediator, concretely.
2. What does `Send(cmd)` do in three steps?
3. Why does a missing handler fail at runtime here, and how would a source-generated mediator change that?
4. Give one concrete situation where adding this mediator would be the *wrong* call.
5. What does behavior registration *order* control?
