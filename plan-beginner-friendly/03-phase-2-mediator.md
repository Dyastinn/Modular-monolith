# Phase 2 — The Mediator (feel the pain first, then fix it)

> 📄 Expert version: [`../plan-Opus/03-phase-2-mediator.md`](../plan-Opus/03-phase-2-mediator.md)

**In one sentence:** We build a "post office" (the mediator) that finds the right handler for any
request — but first we deliberately feel the pain of *not* having one, so you understand *why* it
exists.

**What you'll build:** The mediator contracts (`IRequest`, `IRequestHandler`, `ISender`,
`IPipelineBehavior`), a `Sender` that dispatches requests, and one `LoggingBehavior`. After this,
`/ping` runs through the real mediator.

**Before you start:** Phase 1 skeleton compiles.

> This phase addresses a stated goal: *"I don't understand what problem the mediator solves because
> I've never felt the pain it fixes."* So we build the pain first. **Don't skim Step 1.**

---

## Step 1 — The pain: life without a mediator

Say you have an endpoint that creates an order. The handler that does the work needs several helpers
(repositories, a pricing service, etc.). Without a mediator, the **endpoint itself** has to know about
every one of those helpers and wire them together by hand:

```csharp
app.MapPost("/orders", async (
    CreateOrderRequest req,
    IOrderRepository orders,          // the handler needs this...
    ICustomerRepository customers,    // ...and this...
    IPricingService pricing,          // ...and this...
    IUnitOfWork uow,                  // ...and this...
    ILogger<CreateOrderHandler> log,  // ...and this — all leaking up into the endpoint!
    CancellationToken ct) =>
{
    var handler = new CreateOrderHandler(orders, customers, pricing, uow, log);
    return await handler.Handle(req, ct);
});
```

Look at how much the endpoint has to know: **every dependency of the handler.** The endpoint's job was
supposed to be "receive a web request and pass it on" — but now it's a wiring diagram.

Now imagine you want to add **logging, validation, timing, and a transaction** to *every* endpoint. You
copy-paste the same wrapper code around all fifty of them. Fifty copies that slowly drift apart. Sound
familiar? It's the exact WebForms `Button_Click` sprawl from Phase 0 — just moved to a new location.

**The pain, named precisely:** the caller is *coupled* to how the handler is built, and cross-cutting
code (logging, etc.) is *duplicated everywhere*.

## Step 2 — The obvious-but-insufficient fix

You might think: "I'll make a factory, or a base class, to hide the wiring." That helps a little, but
the caller *still* has to name the specific handler, and it *still* has to explicitly invoke the
logging/validation wrappers at each call site. You want the caller to say only **what** it wants done —
not **how** it's built or **what** wraps it.

## Step 3 — The fix: a mediator (post office)

The mediator flips it around. The endpoint now holds **one** thing — `ISender` — and just sends a
message:

```csharp
app.MapPost("/orders", async (CreateOrderCommand cmd, ISender sender, CancellationToken ct) =>
    await sender.Send(cmd, ct));
```

That's the *entire* endpoint now. When you call `sender.Send(cmd)`, three things happen:

1. It **finds** the handler registered for `CreateOrderCommand`.
2. It **wraps** that handler in the registered pipeline behaviors (logging, validation, transaction) —
   automatically, for *every* message, defined once.
3. It **runs** it and returns the result.

The endpoint no longer knows the handler's dependencies or what wraps it. Want to add metrics to every
handler? Register one new behavior, *once*. Done.

> 💡 **Post office analogy:** You drop a letter in the mailbox (`Send`). You don't know the mail
> carrier's name, their route, or how the sorting works. And the post office automatically weighs and
> stamps every package (the behaviors) — you don't do that per-letter.

## Step 4 — Building it (about 150 lines)

### 4a. The contracts — the "shapes" everyone agrees on

These live in `Erp.Application/Abstractions/Messaging`. They're just interfaces (promises about method
shapes):

```csharp
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

> ℹ️ These names deliberately copy a popular library called **MediatR**. That way, if you ever read
> MediatR code elsewhere, it'll feel familiar. We build our own instead of importing it — building it
> teaches the pattern far better, and avoids any licensing questions.

### 4b. The dispatcher — the tricky part

Here's the hard bit: when someone calls `Send(request)`, the mediator only knows the request is *some*
`IRequest<TResponse>` — it doesn't know the exact type until runtime. It has to *find* the matching
`IRequestHandler` at runtime.

There are two ways to do this. **We build the first (recommended):**

- **Approach A — cache a small wrapper per request type.** The first time it sees a
  `CreateOrderCommand`, it builds a tiny helper that knows how to resolve that command's handler and
  behaviors, and remembers it in a dictionary. Fast after the first call. (This is how MediatR does
  it.)
- **Approach B — use `dynamic`.** Shorter code, but errors hide until runtime and it's slightly slower.

The core of Approach A looks like:

```csharp
public sealed class Sender(IServiceProvider provider) : ISender
{
    private static readonly ConcurrentDictionary<Type, RequestHandlerBase> _wrappers = new();

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
        // Look up (or build once) the wrapper for this exact request type, then run it.
        var wrapper = (RequestHandlerWrapper<TResponse>)_wrappers.GetOrAdd(
            request.GetType(),
            static t => (RequestHandlerBase)Activator.CreateInstance(
                typeof(RequestHandlerWrapperImpl<,>).MakeGenericType(t, typeof(TResponse)))!);

        return wrapper.Handle(request, provider, ct);
    }
}
```

Don't panic if this looks dense — it's the *one* genuinely fiddly file in the project, and you write it
once and rarely touch it again. All it does is: find the handler, wrap it in the behaviors, run it.

> ℹ️ **Honest note on speed:** this uses "reflection" (figuring out types at runtime), which has a
> small cost. It's cached per type, so it's totally fine for a learning project and normal apps. If it
> ever showed up as slow in a profiler, that'd be your cue to switch to a source-generated library like
> `martinothamar/Mediator`. Don't optimize this before you have a reason to.

### 4c. One behavior, to prove the pipeline works

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

### 4d. Registration

At startup, we scan the `Erp.Application` project for all handlers and register each one; register
`ISender` → our `Sender`; and register the behaviors **in the order they should wrap**. **Order
matters** — the first behavior registered is the *outermost* wrapper.

## Step 5 — When is a mediator the WRONG choice?

This is important — patterns aren't free, and a good engineer knows when *not* to use one.

**It's great when:** you have many features, plus cross-cutting concerns (logging, validation,
transactions) that would otherwise be copy-pasted. That's exactly this project.

**It's the wrong call when:**

- You have a tiny app with a handful of endpoints and no cross-cutting concerns. Then the mediator is
  just ceremony — inject the handler directly.
- Your team doesn't know the pattern and the indirection confuses more than it helps.
- You're talking *between separate programs* — that's messaging/queues, a totally different tool. Our
  mediator only works *inside one program*.

**And be honest about its costs even when you do use it:**

- **Indirection.** "Go to definition" on `Send(cmd)` lands on the mediator, not your handler. You learn
  to navigate by request type instead. Some people find this annoying.
- **Errors show up at runtime.** Forget to register a handler? You find out when you run it, not when
  you compile. (Source-generated mediators catch this at build time; ours doesn't.)
- **It's not magic decoupling.** The mediator gives you a *place* to put clean code — it doesn't make
  bad handlers good.

---

## ✅ Check Yourself

1. **Describe, concretely, the coupling and duplication pain you get *without* a mediator.** *(Hint:
   the endpoint knowing every dependency; fifty copies of logging.)*
2. **What does `Send(cmd)` do, in three steps?** *(Find handler → wrap in behaviors → run.)*
3. **Why does a missing handler fail at runtime here, and how would a source-generated mediator change
   that?**
4. **Give one concrete situation where adding this mediator would be the *wrong* call.**
5. **What does the *order* you register behaviors control?** *(Which one is the outermost wrapper.)*
