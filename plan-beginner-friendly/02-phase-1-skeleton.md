# Phase 1 — Build the Empty Frame First (the "Walking Skeleton")

> 📄 Expert version: [`../plan-Opus/02-phase-1-skeleton.md`](../plan-Opus/02-phase-1-skeleton.md)

**In one sentence:** Create the folders and projects, then prove the whole pipe works with one
trivial feature — *before* adding any real complexity.

**What you'll build:** A solution that compiles, the full folder tree, and a `/ping` feature that goes
web request → mediator → handler → responds `"pong"`. That's it.

**Before you start:** Phase 0 read. .NET 10 SDK installed. SQL Server reachable (LocalDB is fine).

---

## Why build something so useless first?

Imagine you assemble the database, the domain model, the mediator, and CQRS all at once — and it
doesn't work. **Which part is broken?** You have no idea. You'd be debugging five new things at once.

A **walking skeleton** is the thinnest possible slice that touches the *whole pipeline* but does
almost nothing. Once `GET /ping` returns `"pong"` through the real machinery, you've *proven the pipe*.
Every real feature after this is just a fancier version of a path you already know works.

> 💡 **Analogy:** Before decorating a house, you frame it and confirm one lightbulb turns on. If the
> light works, you know the wiring and breaker panel are sound. Now you can add rooms confidently.

## 1. Four projects (the four layers, made real)

We create four code projects under `src/` and three test projects under `tests/`:

```
Erp.sln                          <- the "solution" (ties all projects together)
├── src/
│   ├── Erp.Domain/              <- business objects & rules. Depends on NOTHING.
│   ├── Erp.Application/         <- the feature slices + the mediator's contracts
│   ├── Erp.Infrastructure/      <- database code, mediator's guts
│   └── Erp.Api/                 <- the website host; wires everything together
└── tests/
    ├── Erp.Domain.Tests/        <- (used in Phase 8)
    ├── Erp.Application.Tests/   <- (used in Phase 8)
    └── Erp.Integration.Tests/   <- (used in Phase 8)
```

Now the important part — **which project is allowed to reference which:**

```
Erp.Api            can use →  Erp.Application, Erp.Infrastructure
Erp.Application    can use →  Erp.Domain
Erp.Infrastructure can use →  Erp.Application, Erp.Domain
Erp.Domain         can use →  (nothing!)
```

> 🛡️ **This is a safety net, not red tape.** If someone ever tries to use a database library inside
> `Erp.Domain`, the project *won't compile*. That compile error is a **feature** — it physically stops
> the pure business code from getting polluted with database concerns. The computer enforces the rule
> so you don't have to remember it.

> **On "modules":** the plan says each department (Orders, HR, Accounting) could someday become its own
> program. For now, everything Order-related just lives in an `Orders` *folder* inside each project —
> not separate projects. Folders are free to rearrange; splitting into separate programs is expensive.
> We keep it cheap until we actually need it (Phase 9 explains the upgrade path).

## 2. The folder tree, with every folder justified

Here's the target layout. For each folder, ask: *why does it exist, and why does code live here and
not somewhere else?*

```
src/Erp.Domain/
    Common/          # shared building blocks: base Entity, AggregateRoot, IDomainEvent.
                     # WHY HERE: pure business concepts, zero database/web types. (filled in Phase 3)
    Orders/          # the Ordering department's business objects. (filled in Phase 3)

src/Erp.Application/
    Abstractions/Messaging/   # the mediator's "contracts": IRequest, IRequestHandler, ISender...
                              # WHY HERE: the app layer defines the SHAPE of its tasks;
                              #           Infrastructure builds the actual dispatcher.
    Behaviors/                # cross-cutting steps (logging now; validation in Phase 4)
    Orders/
        Ping/                 # ← our walking-skeleton feature
            PingQuery.cs
            PingHandler.cs
                              # WHY: grouped by department (Orders), then by task (Ping).
                              #      This is the VSA spine — one folder per task.

src/Erp.Infrastructure/
    Messaging/
        Sender.cs             # the hand-built mediator (Phase 2)
        DependencyInjection.cs
    Persistence/              # database connection, stored-proc callers, repositories (Phase 4)

src/Erp.Api/
    Endpoints/Orders/
        PingEndpoint.cs       # maps GET /orders/ping → the mediator
    Program.cs                # the "composition root": wires up everything at startup
    appsettings.json
    appsettings.Development.json
```

**Two placement rules to burn into your memory now:**

1. **A task's code lives in its task folder** (`Application/Orders/CreateOrder/`), **not** spread
   across generic `Commands/`, `Validators/`, `Handlers/` folders. Grouping by technical *type* is the
   old layered anti-pattern VSA rejects — it scatters one feature all over the tree.
2. **Anything shared by many tasks moves down into a shared layer** — to `Domain` if it's a rule or
   business object, to `Infrastructure` if it's plumbing. The `Order` object is used by many tasks, so
   it lives in `Domain`, *not* inside the `CreateOrder` folder.

## 3. The `/ping` feature (the whole skeleton)

`PingQuery` is a request that returns a `string`. `PingHandler` just returns `"pong"`. No database, no
business logic. Its *only* job is to prove: web endpoint → mediator finds the right handler → response
comes back.

```csharp
// Application/Orders/Ping/PingQuery.cs
public sealed record PingQuery : IRequest<string>;

// Application/Orders/Ping/PingHandler.cs
public sealed class PingHandler : IRequestHandler<PingQuery, string>
{
    public Task<string> Handle(PingQuery request, CancellationToken ct)
        => Task.FromResult("pong");
}

// Api/Endpoints/Orders/PingEndpoint.cs  (a "minimal API" endpoint)
app.MapGet("/orders/ping",
    async (ISender sender, CancellationToken ct)
        => await sender.Send(new PingQuery(), ct));
```

> ⚠️ **These interfaces (`IRequest`, `IRequestHandler`, `ISender`) don't exist yet** — we build them
> in Phase 2. **Recommended:** do Phase 1 and Phase 2 back-to-back so `/ping` runs through the real
> mediator immediately. If you must pause between them, you can temporarily call `PingHandler`
> directly — but clearly mark that as throwaway code.

## 4. What to produce in this phase

- The four `src` projects + three `tests` projects, with the reference directions from §1.
- The folder tree (empty folders can hold a `.gitkeep` placeholder).
- The `Ping` query, handler, and endpoint.
- `Program.cs` wired so the app starts and `GET /orders/ping` responds.
- A short `README` at the repo root pointing at these phase docs.

**Explicitly NOT now:** no domain model, no database, no stored procedures, no validation, no real
business logic. If you feel the urge to add any of it — don't. That urge is the thing this phase trains
you to resist.

## 5. Common mistakes to avoid

- **Over-building the skeleton.** If `/ping` touches a database, it's not a skeleton anymore. Keep it
  dumb.
- **Making generic `Commands/` or `Handlers/` folders.** This is the #1 reflex from old tutorials and
  it quietly destroys the whole benefit of VSA. Group by *task*, always.
- **Letting the Domain project reference a database library.** If it compiles with one, you've broken
  the dependency rule. (You'll only notice if you actually check — so check.)

---

## ✅ Check Yourself

1. **Why does `Erp.Domain` reference nothing, and what helpfully breaks if it references a database
   library?** *(Hint: a compile error that protects the pure code.)*
2. **Why group `Application` code by task instead of by `Commands/`/`Handlers/`?** *(Hint: the taco
   drawer.)*
3. **Why build a `/ping` feature that does nothing useful before building `CreateOrder`?** *(Hint:
   prove the pipe with one bulb.)*
4. **Where does the shared `Order` object live, and why not inside the `CreateOrder` folder?** *(Hint:
   it's shared by many tasks.)*
