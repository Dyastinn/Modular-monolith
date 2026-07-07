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

A **mediator** — a single object that sits between your endpoints and your business logic. It's an
implementation of the **Mediator Pattern**, whose goal is to reduce coupling between parts of your
application. Instead of one class knowing about and calling many other classes directly, everything sends
its request to the mediator, and the mediator finds the correct handler to perform the work.

Think of it as a **post office**. You don't need to know who delivers your letter or where they sit. You
simply hand your letter to the post office, and it routes it to the right destination. In the same way, an
endpoint doesn't know—or care—which class handles "Create Order" or "Get Customer." It simply hands the
request to the mediator.

Without a mediator, an endpoint would directly depend on the services it needs:

```
Endpoint ├──► OrderService
         ├──► CustomerService
         ├──► CreditService
         └──► InventoryService
```

As features grow, endpoints become coupled to more and more services.

With a mediator, every endpoint depends on **one thing**:

```
Endpoint ──► Mediator ──► Correct Handler
```

The endpoint simply says, "Here's a request." The mediator finds the matching handler and executes it. That
keeps endpoints small, removes knowledge of who performs the work, and gives every request the same
consistent pipeline.

We'll build the shared contracts (`ICommand`, `IQuery`, `ISender`, …), pick and wire an implementation, and
finish with a live proof: a `GET /sales/ping` that travels *endpoint → mediator → handler* and returns
`"pong"`. That's the whole pipeline working end-to-end — the walking skeleton for every real feature that
follows.

**You have two options for the implementation, and you pick one now:**

- **Option A — hand-roll it (recommended here):** write ~150 lines of your own mediator, so you see exactly
  how a request finds its handler. High learning value; it's *your* code.
- **Option B — use the free library `martinothamar/Mediator`:** a MIT-licensed, source-generated NuGet
  package with a MediatR-shaped API. Less to write; a compile-time-checked black box.

The rest of the tutorial's code works with **either**, because both expose the same interface names. §2
explains the trade-offs so you can choose the implementation that best fits your goals.

## 2. Wait... isn't this CQRS?

Sharp eye—it is, at least partly.

**CQRS** stands for **Command Query Responsibility Segregation**. At its core, it says:

- **Commands** change the system.
- **Queries** read from the system.
- Don't mix the two.

That's exactly why this tutorial introduces `ICommand` and `IQuery`.

However, **CQRS is a spectrum, not a switch**. Some teams stop at separating commands from queries in code.
Others go much further, using separate databases, separate read models, message queues, or even event
sourcing.

This tutorial uses the **simple end of the spectrum**:

- Commands and queries have different types.
- They have different handlers.
- They travel through the same mediator.
- They use the same database.

We're **not** building the kind of CQRS architecture that duplicates data into separate read stores. This
is still a **modular monolith**, not a distributed system.

Think of it this way:

- **CQRS** answers: *"Is this request changing data or reading data?"*
- **Mediator** answers: *"How do I find the correct handler for this request?"*

The two patterns solve different problems, but they work together so well that you'll often see them used
side by side.

## 3. Why this is a real decision (not just ceremony)

There are two perfectly valid ways to use the Mediator pattern:

- write your own mediator
- use a well-tested library

Neither is universally "correct." The trade-off is between **understanding** and **writing less infrastructure**.

Since this tutorial is designed to explain how the architecture works—not just how to consume it—we'll compare both approaches before choosing one.

| | **Hand-rolled (own)** | **Free library: `martinothamar/Mediator`** |
|---|---|---|
| What it is | ~150 lines you write and own | A NuGet package that generates the dispatch code at compile time |
| Cost | Free; your maintenance | Free (MIT — *verify the LICENSE yourself*); their maintenance |
| Speed | Reflection, cached per type — fine for normal loads | **Source-generated** → no reflection, very fast |
| Errors for a missing handler | **Runtime** (you find out when you call it) | **Compile-time** warning (found at build) |
| Learning value | High — you see exactly how it works | Lower — it's a black box |
| Risk | A bug is *your* bug | Depends on a third party; API caveats (see below) |
| Control | Total | You bend to their conventions |

> ℹ️ **Why not MediatR (the famous one)?** MediatR moved to a paid license for larger companies in 2025.
> It's still free under a revenue threshold, but this tutorial avoids it to keep things unambiguously free
> while still teaching the Mediator pattern. `martinothamar/Mediator` is a genuinely free, MIT-licensed,
> source-generated alternative with a MediatR-shaped API.

**My recommendation for *this* tutorial:** **hand-roll it** (Option A). Not because it's better in
production, but because *understanding what's inside the box* is the entire point of a learning project,
and our load is tiny. Then in [Phase 13](13-wrap-up-checklist.md) we note exactly when you'd swap to the
library. If you'd rather use the library from day one, Option B is fully wired below and nothing else in
the tutorial changes.

## 4. The shared contracts (both options use these)

Before we build a mediator, we first need to define its **public API**—the set of interfaces that every
endpoint, handler, and module agrees to use. These contracts describe **what** the mediator can do, not
**how** it does it.

Whether you hand-roll the mediator or use a library, the rest of the solution depends only on these
contracts. That's what makes the two implementations interchangeable.

We'll build the mediator in three files:

```text
Erp.Shared.Messaging
│
├── Contracts.cs          // shared interfaces
├── Sender.cs             // the mediator implementation
└── Behaviors
    └── LoggingBehavior.cs
```

We'll build them one at a time.

### Step 1 — Install the packages

> 📦 **Packages for this phase — add these now.** This is the phase that first needs a mediator,
> validation, cross-cutting behaviors, and web endpoints, so here's where those packages come in (following
> Phase 1's "install only when needed" rule). Each `dotnet add` also adds a `<PackageVersion>` entry to
> `Directory.Packages.props`.

>
>
> - **`Microsoft.Extensions.DependencyInjection.Abstractions`** — provides the dependency injection
>   interfaces (`IServiceCollection`) that our registration methods extend.
> - **`Microsoft.Extensions.Logging.Abstractions`** — provides `ILogger<T>` without depending on ASP.NET's
>   concrete logging implementation.
> - **`FluentValidation`** — lets us validate commands and queries before they reach their handlers.
> - **`FluentValidation.DependencyInjectionExtensions`** — adds `AddValidatorsFromAssembly()`, which
>   automatically registers every validator in a module.
> - **`Carter`** — lets each feature define its own endpoints as ICarterModule instead of putting all
>   routes in `Program.cs`.
>

>

Install them where they're first needed:


```bash
# The mediator contracts + behaviors need DI, logging, and validation abstractions:
dotnet add src/Shared/Erp.Shared.Messaging package Microsoft.Extensions.DependencyInjection.Abstractions
dotnet add src/Shared/Erp.Shared.Messaging package Microsoft.Extensions.Logging.Abstractions
dotnet add src/Shared/Erp.Shared.Messaging package FluentValidation

# The shared kernel hosts the mediator + Carter wiring:
dotnet add src/Shared/Erp.Shared.Infrastructure package Carter
dotnet add src/Shared/Erp.Shared.Infrastructure package Microsoft.Extensions.DependencyInjection.Abstractions

# The Sales module gets Carter (its endpoints), FluentValidation, and the assembly-scan helper:
dotnet add src/Modules/Sales package Carter
dotnet add src/Modules/Sales package FluentValidation
dotnet add src/Modules/Sales package FluentValidation.DependencyInjectionExtensions
dotnet add src/Modules/Sales package Microsoft.Extensions.DependencyInjection.Abstractions

# The host maps every module's Carter endpoints:
dotnet add src/Erp.Api package Carter
```

> ⚠️ **Carter needs the ASP.NET shared framework.** A feature's `XxxEndpoint.cs` is a Carter
> `ICarterModule` that lives inside the Sales module and compiles against ASP.NET routing types such as
> `IEndpointRouteBuilder`. Those come from a `FrameworkReference`, not a NuGet package. Add this manually
> to both `Erp.Modules.Sales.csproj` and `Erp.Shared.Infrastructure.csproj`:

```xml
 <ItemGroup>
   <FrameworkReference Include="Microsoft.AspNetCore.App" />
 </ItemGroup>
```

> Without it, the endpoint files won't compile. (Endpoints stay in `Features/`, never in the pure `Domain/`
> — the domain must not see Carter or ASP.NET.)


### Step 2 — Create `Contracts.cs`

Create a new file named **`Contracts.cs`** in `src/Shared/Erp.Shared.Messaging`.

> 💡 **Why one file?** These contracts are all part of the same concept: the mediator's public API. Keeping
> them together makes them easier to learn because you can see the entire contract in one place. As the
> project grows, you may choose to split them into folders like `Commands/`, `Queries/`, and
> `Abstractions/`, but for this tutorial a single `Contracts.cs` keeps the pattern easy to understand.

They're just **interfaces**—promises about what shape something has, not how it's implemented.



Let's decode them before writing the code:

> 💡 **New to generics?** `TResponse`, `TRequest`, `TCommand`, and `TQuery` are all **generic type
> parameters**. A **generic** is simply a placeholder for a type, allowing one interface or class to work
> with many different types.
>
> `T` stands for **Type**, so:
>
> - `TResponse` = the type returned by a request.
> - `TRequest` = the request being handled.
> - `TCommand` = the command being handled.
> - `TQuery` = the query being handled.
>
> For example, `IRequest<string>` returns a `string`, while `IRequest<SalesOrderDto>` returns a
> `SalesOrderDto`.

- **`IRequest<TResponse>`** — the foundation of the mediator. It simply means, **"I'm a request that, when
  handled, produces a `TResponse`."** Every request in the system starts here.
- **`ICommand<TResponse>`** — extends `IRequest<TResponse>` and represents a request that **changes the
  system**. It adds no behavior; it simply tells both the reader and the compiler, "this is a command."
- **`IQuery<TResponse>`** — also extends `IRequest<TResponse>`, but represents a request that **reads
  data** without changing the system.
- **`Unit`** — the equivalent of `void` for commands. Since `Task<void>` doesn't exist, commands that don't
  return meaningful data instead return `Task<Unit>`.
- **`IRequestHandler<TRequest, TResponse>`** — the foundation for all handlers. It says, **"I know how to
  handle one specific `IRequest<TResponse>`."** Every request has exactly one handler.
- **`ICommandHandler<TCommand, TResponse>`** — extends `IRequestHandler` but accepts only commands. It adds
  no behavior; it simply makes intent obvious and lets us target commands specifically later.
- **`IQueryHandler<TQuery, TResponse>`** — extends `IRequestHandler` but accepts only queries, giving us
  the same benefits for read operations.
- **`RequestHandlerDelegate<TResponse>`** — a `delegate` is simply "a variable that holds a method you can
  call later." Behaviors use it to invoke the next step in the pipeline.
- **`IPipelineBehavior<TRequest, TResponse>`** — a wrapper around every request. Logging, validation,
  transactions, caching, and other cross-cutting concerns plug in here.
- **`ISender`** — the only interface endpoints use. They simply call `Send(...)`; the mediator finds the
  correct handler and runs it.

Now add the following code:

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

> 📎 Line-by-line explanation of each of these interfaces is also in [beginner Phase 2 §4a](../plan-beginner-friendly/03-phase-2-mediator.md).

> ❓ **"Why so many interfaces for something simple?"** Each interface is a *seam*—a clean place where two
> parts meet without knowing each other's implementation. `ISender` lets endpoints stay ignorant of
> handlers. `IRequestHandler` lets the mediator execute any request without knowing what it does.
> `IPipelineBehavior` lets you add logging, validation, transactions, and other cross-cutting concerns
> without touching a single handler. The interfaces *are* the decoupling.

## 5. Why `ICommand` and `IQuery` markers exist

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

Earlier we defined the **contracts** (`IRequest`, `IRequestHandler`, `ISender`, ...). Those described
**what** a mediator can do. Now we'll build **how** it does it.

Our mediator has one job:

1. Receive a request.
2. Find the correct handler.
3. Let any registered behaviors wrap that handler.
4. Execute the pipeline.
5. Return the result.

We'll build all of that in a single file:

```text
Erp.Shared.Messaging
└── Sender.cs
```

The entire flow looks like this:

```
Endpoint
    │
    ▼
ISender.Send(request)
    │
    ▼
Resolve the correct handler
    │
    ▼
Wrap it in behaviors
    │
    ▼
Execute
    │
    ▼
Return the response
```

Now let's build it.

```csharp
// Sender.cs
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Shared.Messaging;

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

### Understanding the code

The file is longer than it looks because it contains both the mediator itself and a few helper classes.
Don't worry about reading it top-to-bottom yet. The important part is understanding **what happens when
`Send()` is called**.

#### Step 1 — A request arrives

Everything starts here:

```csharp
public Task<TResponse> Send<TResponse>(
    IRequest<TResponse> request,
    CancellationToken ct = default)
```

The endpoint doesn't call a handler directly. Instead it gives the request to the mediator:

```
CreateOrderCommand
        │
        ▼
Sender.Send(...)
```

At this point the mediator knows:

- the request object
- the response type (`TResponse`)

It still doesn't know **which handler** should execute it.

---

#### Step 2 — Find the correct handler

Eventually the mediator reaches this line:

```csharp
var handler =
    sp.GetRequiredService<IRequestHandler<TRequest, TResponse>>();
```

Dependency Injection has already registered every handler.

That means asking for

```
IRequestHandler<CreateOrderCommand, Unit>
```

returns

```
CreateOrderCommandHandler
```

and asking for

```
IRequestHandler<GetOrderQuery, OrderDto>
```

returns

```
GetOrderQueryHandler
```

The mediator never hard-codes those classes.

---

#### Step 3 — Build the pipeline

The handler becomes the innermost piece of work:

```csharp
RequestHandlerDelegate<TResponse> pipeline =
    () => handler.Handle(...);
```

Think of `pipeline` as

```
Handler
```

Then every registered behavior wraps it.

Suppose we register:

```
Logging
Validation
```

The pipeline becomes

```
Logging
    ▼
Validation
    ▼
Handler
```

Each behavior receives a `next` delegate representing **"everything after me."**

Calling

```csharp
await next();
```

moves to the next behavior.

Eventually the handler executes.

---

#### Step 4 — Run the pipeline

Finally:

```csharp
return pipeline();
```

Although `pipeline` is just a delegate, it now represents the entire chain:

```
Logging
    ▼
Validation
    ▼
Handler
```

Calling it once executes the entire request.

---

#### What about `MakeGenericType()` and `Activator.CreateInstance()`?

Those lines are **not** the Mediator Pattern itself.

They're simply an optimization that lets one `Sender` work with any request type without writing a huge
`switch` statement.

The first time a request type is seen, the mediator uses reflection to build a small helper object
(`RequestHandlerWrapperImpl<TRequest, TResponse>`).

That helper is stored in a `ConcurrentDictionary`, so the reflection only happens **once per request type**.

Every later request reuses the cached helper instead of rebuilding it.

Think of it like this:

```
First request
    │
    ▼
Reflection
    │
    ▼
Create wrapper
    │
    ▼
Cache it

Later requests
    │
    ▼
Use cached wrapper
```

The reflection code exists for performance. The mediator algorithm itself is still just:

```
Resolve handler
      ▼
Wrap in behaviors
      ▼
Execute
```

> 💡 The most important part of this file isn't the reflection—it's the **request pipeline**.
>
> When `Send()` is called, the mediator does four things:
>
> 1. **Resolve** the correct handler from Dependency Injection.
> 2. **Build** a pipeline with the handler at the center.
> 3. **Wrap** that handler in every registered behavior (logging, validation, etc.).
> 4. **Execute** the pipeline and return the response.
>
> The `MakeGenericType()` and `Activator.CreateInstance()` calls exist only to build and cache a helper
> object for each request type. They let one `Sender` work with any request without writing a giant
> `switch` statement, and the reflection only happens the **first** time each request type is seen. The
> mediator pattern itself is simply: **resolve handler → wrap in behaviors → run**.

### Registration — teaching the mediator where to find handlers

Our `Sender` can execute requests, but it still needs one piece of information:

> **How does it know which handler belongs to which request?**

The answer is **Dependency Injection (DI)**.

When `Sender` reaches this line:

```csharp
var handler = sp.GetRequiredService<IRequestHandler<TRequest, TResponse>>();
```

it asks ASP.NET Core's DI container:

> "Give me the handler for this request."

That only works if we've registered every handler when the application starts.

Registration naturally splits along the module boundary, which is exactly what we want in a modular
monolith:

- The **shared infrastructure** registers the mediator itself.
- Each **module** registers its own handlers, validators, and services.

This keeps the module boundaries intact. The Sales module only registers Sales handlers, the Inventory
module only registers Inventory handlers, and so on. The mediator doesn't know about any of them—it simply
asks Dependency Injection for the correct handler.

First, register the mediator itself once in `Erp.Shared.Infrastructure`:

```csharp
public static IServiceCollection AddMessaging(this IServiceCollection services)
{
    services.AddScoped<ISender, Sender>();

    // Cross-cutting behaviors (logging, validation, etc.) plug into this pipeline later.
    // We don't register any behaviors yet because we haven't built them.
    return services;
}

// AddSharedInfrastructure() calls AddMessaging().
```

> 🔑 **The pipeline is built now; the behaviors come later.** The `Sender` already supports
> IPipelineBehavior — we simply haven't registered any concrete ones yet. When we add logging,
> validation, and other cross-cutting concerns in Phase 11, they'll automatically wrap every request
> without changing the mediator itself.

Next, each module registers its own handlers. Rather than registering every handler manually, we scan the
module's assembly and automatically register every class that implements `IRequestHandler<,>`.

```csharp
// Modules/Sales/DependencyInjection.cs
public static class DependencyInjection
{
    public static IServiceCollection AddSalesModule(
        this IServiceCollection services,
        IConfiguration config)
    {
        // Look only inside the Sales module.
        var assembly = typeof(DependencyInjection).Assembly;

        // Register every IRequestHandler<,> defined in this module.
        var handlerTypes = assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType &&
                            i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
                .Select(i => new
                {
                    Implementation = t,
                    Service = i
                }));

        foreach (var handler in handlerTypes)
        {
            services.AddTransient(handler.Service, handler.Implementation);
        }

        // Register every FluentValidation validator in this module.
        services.AddValidatorsFromAssembly(assembly);

        // Later phases register repositories, services, etc.
        return services;
    }
}
```

> 💡 **Why `typeof(DependencyInjection).Assembly`?** We need a way to tell .NET, "Scan only this module."
> Any type declared in the Sales project would work. We use `DependencyInjection` simply because we're
> already inside that file.

> ⚠️ **`AddValidatorsFromAssembly()` needs an extra package.** That helper comes from
> **`FluentValidation.DependencyInjectionExtensions`**, not the base `FluentValidation` package. That's
> why we installed it earlier in this phase. There are no validators yet, so this call simply registers
> nothing until we create our first one.

Notice that no module registers another module's handlers. The module boundaries enforced by the project
references are also enforced by Dependency Injection. Finally, `Program.cs` brings everything together by
calling `AddSharedInfrastructure()` once, followed by each module's `Add*Module()` method.

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

## 6. Wire the host, then prove it — your checkpoint ✅

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

Every feature sends a **request object** to the mediator. The handler always receives a single request object containing all the data it needs.

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

`PingRequest` uses the **record primary constructor** syntax. It's shorthand for a record with a constructor and a property. The compiler generates those for you.

This:

```csharp
public sealed record PingRequest(string Name) : IRequest<string>;
```

is roughly equivalent to:

```csharp
public sealed record PingRequest : IRequest<string>
{
    public string Name { get; init; }

    public PingRequest(string name)
    {
        Name = name;
    }
}
```

The endpoint creates a `PingRequest`, and the mediator passes that same object to the handler.

`src/Modules/Sales/Features/Ping/PingEndpoint.cs`:

```csharp
public sealed class PingEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet("/sales/ping", async (string name, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new PingRequest(name), ct)));
}
```

Conceptually, this is what happens:

```text
HTTP Request
      │
      ▼
PingEndpoint
      │
      │ new PingRequest(name)
      ▼
ISender.Send(request)
      │
      ▼
PingHandler.Handle(request, ct)
      │
      ▼
"pong: John"
```

Although `PingRequest` only has one property here, real requests often contain many properties (such as `CustomerId`, `OrderId`, or `Quantity`). Using a request object keeps the handler signature consistent regardless of how much data is needed.

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

## 7. How a request flows through the mediator (either option)

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

<!-- ## 8. Why each component exists

| Component | Why it exists |
|---|---|
| `ISender` | The single dependency an endpoint needs. Endpoints stay ignorant of handlers and their dependencies. |
| `IRequestHandler<,>` | One handler per use case; trivially unit-testable as a plain class. |
| `IPipelineBehavior<,>` | Cross-cutting concerns (logging, validation, later maybe transactions) written **once**, applied to all. |
| `Sender` (Option A) *or* the library (Option B) | The engine that resolves the handler and composes the behaviors. Interchangeable. |
| `ICommand`/`IQuery` | Visible intent + a hook for command-only behaviors. |
| `Unit` | Lets commands that change state but return nothing still fit the `IRequest<TResponse>` shape. | -->

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
