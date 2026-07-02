# Phase 1 — Solution Skeleton + Walking Skeleton

**Goal:** create the project structure and prove the plumbing with one trivial end-to-end slice
*before* any real complexity exists.
**Produces:** a compiling solution, the folder tree, and a `Ping`/health slice that goes
HTTP → mediator → handler → response.
**Prereq:** Phase 0 read. .NET 10 SDK installed. SQL Server reachable (LocalDB or a dev instance).

> **Why a "walking skeleton" first?** If you introduce CQRS, DDD, Dapper, and the mediator all at
> once, and it doesn't work, you can't tell *which* part is wrong. A walking skeleton is the thinnest
> possible slice that exercises the whole pipe. Once `GET /ping` returns "pong" through the mediator,
> every later slice is a variation on a proven path.

---

## 1. Solution and projects

Four projects. This is the Clean Architecture layering from the overview, made concrete.

```
Erp.sln
├── src/
│   ├── Erp.Domain/            <- entities, value objects, domain events, domain interfaces. No dependencies.
│   ├── Erp.Application/       <- vertical feature slices, the mediator abstractions, behaviors.
│   ├── Erp.Infrastructure/    <- Dapper, connection factory, stored-proc callers, repositories, dispatchers.
│   └── Erp.Api/               <- ASP.NET Core host, endpoints, DI composition root.
└── tests/
    ├── Erp.Domain.Tests/          <- unit (Phase 8)
    ├── Erp.Application.Tests/     <- unit for validators/handlers (Phase 8)
    └── Erp.Integration.Tests/     <- real DB (Phase 8)
```

**Project reference direction (enforces the dependency rule):**

```
Erp.Api            -> Erp.Application, Erp.Infrastructure
Erp.Application    -> Erp.Domain
Erp.Infrastructure -> Erp.Application, Erp.Domain
Erp.Domain         -> (nothing)
```

If `Erp.Domain` ever needs a reference to Dapper or ASP.NET, something is in the wrong project.
That compile error is a *feature* — it stops the domain from getting polluted.

> **Note on "one module":** the brief says each module is a department that could become a service.
> For now everything Order-related lives under an `Orders` folder inside each project (not separate
> projects). Phase 9 explains why folder-level module boundaries are enough until extraction, and
> what you'd change to promote a module to its own assembly/service.

## 2. Folder tree, with every folder defended

Below is the target tree with the walking-skeleton slice in place. Each folder answers:
*why does it exist, why is code here, why not elsewhere?*

```
src/Erp.Domain/
    Common/
        # WHY: primitives shared by all aggregates — base Entity, AggregateRoot, IDomainEvent,
        #      strongly-typed ID structs live near the aggregates that own them (see Phase 3).
        # WHY HERE: pure domain concepts, no persistence or framework types.
    Orders/
        # WHY: the Ordering module's domain lives here. Populated in Phase 3.
        # (empty in Phase 1 except a placeholder)

src/Erp.Application/
    Abstractions/
        Messaging/
            # WHY: IRequest, IRequestHandler, ISender, IPipelineBehavior — the mediator contract.
            # WHY HERE: the Application layer defines the shape of its own use cases; Infrastructure
            #           implements the dispatcher. Domain must not know about it.
    Behaviors/
        # WHY: cross-cutting pipeline behaviors (logging in Phase 2; validation in Phase 4).
    Orders/
        Ping/                          <- the walking-skeleton slice
            PingQuery.cs
            PingHandler.cs
        # WHY: features grouped by module (Orders), then by use case (Ping). This is the VSA spine:
        #      each use case folder will later hold its command/query + validator + handler.

src/Erp.Infrastructure/
    Messaging/
        Sender.cs                      <- the hand-rolled mediator implementation (Phase 2)
        DependencyInjection.cs         <- handler + behavior registration
        # WHY: the dispatcher is an implementation detail (reflection, DI lookup) — infrastructure.
    Persistence/
        # WHY: IDbConnectionFactory, Dapper setup, stored-proc callers, repositories (Phase 4).
        # Empty in Phase 1.

src/Erp.Api/
    Endpoints/
        Orders/
            PingEndpoint.cs            <- maps GET /orders/ping -> ISender.Send(new PingQuery())
        # WHY: endpoints are thin adapters. They translate HTTP <-> messages and nothing else.
    Program.cs                         <- composition root: wires DI, mediator, endpoints.
    appsettings.json
    appsettings.Development.json       <- Development connection string (Phase 4)
```

**Two placement rules to internalize now:**

- **A use case's code lives in its use-case folder** (`Application/Orders/<UseCase>/`), *not* spread
  across generic `Commands/`, `Validators/`, `Handlers/` folders. Grouping by *technical kind* is the
  layered anti-pattern VSA rejects — it scatters one feature across the tree.
- **Anything shared by multiple use cases moves down a level** (to `Domain` if it's a rule/model,
  to `Infrastructure` if it's plumbing). The `Order` aggregate is shared by many slices, so it's in
  `Domain`, not inside `CreateOrder`.

## 3. The walking-skeleton slice

`PingQuery` is an `IRequest<string>`; `PingHandler` returns `"pong"`. No DB, no domain. Its only job
is to prove: endpoint → `ISender.Send` → correct handler resolved → response returned.

Illustrative shapes (final interfaces defined in Phase 2 — here they're placeholders so the skeleton
compiles):

```csharp
// Application/Orders/Ping/PingQuery.cs
public sealed record PingQuery : IRequest<string>;

// Application/Orders/Ping/PingHandler.cs
public sealed class PingHandler : IRequestHandler<PingQuery, string>
{
    public Task<string> Handle(PingQuery request, CancellationToken ct) => Task.FromResult("pong");
}

// Api/Endpoints/Orders/PingEndpoint.cs (minimal API)
app.MapGet("/orders/ping",
    async (ISender sender, CancellationToken ct) => await sender.Send(new PingQuery(), ct));
```

Until Phase 2 exists, you may temporarily register `PingHandler` directly and call it without a real
`Sender`. **Preferred:** do Phase 1 and Phase 2 back to back so the skeleton runs through the real
mediator immediately. If you split them, keep Phase 1's direct-call version clearly marked as
throwaway.

## 4. What Sonnet should produce in this phase

- The four `src` projects + three `tests` projects, with the reference directions above.
- The folder tree (empty folders can hold a `.gitkeep` or a placeholder file).
- The `Ping` slice and endpoint.
- `Program.cs` wired so the app starts and `GET /orders/ping` responds (via direct handler call if
  Phase 2 isn't done yet; via the mediator if it is).
- A short `README` in the repo root pointing at these phase docs.

**Explicitly NOT in this phase:** no domain model, no Dapper, no stored procs, no validation, no real
business logic. Resist all of it.

## 5. Pitfalls

- **Over-building the skeleton.** If the `Ping` slice touches a database, it's not a skeleton anymore.
- **Generic `Commands/`/`Handlers/` folders.** This is the most common reflex from layered tutorials
  and it quietly kills the VSA benefit. Group by use case.
- **Domain project sprouting a Dapper reference.** If it compiles with one, you've broken the
  dependency rule; the compiler won't warn you unless the reference is genuinely absent.

---

## Checkpoint — you understand this phase when you can answer:

1. Why does `Erp.Domain` reference nothing, and what breaks (helpfully) if it references Dapper?
2. Why do we group `Application` code by use case instead of by `Commands/`/`Handlers/`?
3. Why build a `Ping` slice that does nothing useful before building `CreateOrder`?
4. Where would the shared `Order` aggregate live, and why not inside the `CreateOrder` folder?
