# Phase 1 — Solution Skeleton + Walking Skeleton

**Goal:** create the modular project structure and prove the plumbing with one trivial end-to-end slice
*before* any real complexity.
**Produces:** a compiling solution, the folder tree, and a `Ping` slice: HTTP → Carter → mediator → handler
→ response.
**Prereq:** Phase 0 read. .NET 10 SDK. SQL Server reachable (LocalDB fine).

> **Why a walking skeleton first?** Introduce CQRS, DDD, Dapper, and the mediator all at once and, if it
> breaks, you can't tell which part is wrong. Once `GET /sales/ping` returns "pong" through the mediator,
> every later slice is a variation on a proven path.

---

## 1. Solution and projects

Split by **capability**, not technical layer: a Shared kernel, one project per module, and a host.

```
Erp.slnx
├── src/
│   ├── Shared/
│   │   ├── Erp.Shared.BuildingBlocks   <- Entity, AggregateRoot, ValueObject, IDomainEvent. No deps.
│   │   ├── Erp.Shared.Common           <- Result/Error, IClock, paging. No deps.
│   │   ├── Erp.Shared.Messaging        <- mediator contracts + Sender + behaviors (Phase 2)
│   │   └── Erp.Shared.Infrastructure   <- IDbConnectionFactory, Carter/auth/error wiring, AddSharedInfrastructure()
│   ├── Modules/
│   │   └── Sales/  (Erp.Modules.Sales) <- Domain/ Features/ Infrastructure/ DependencyInjection.cs
│   └── Erp.Api                         <- host / composition root
└── tests/
    ├── Erp.Modules.Sales.UnitTests
    └── Erp.Integration.Tests
```

**Project reference direction (enforces the two rules):**

```
Erp.Api                   -> each Module, Erp.Shared.Infrastructure
Erp.Modules.Sales         -> Erp.Shared.{BuildingBlocks, Common, Messaging, Infrastructure}
Erp.Modules.Inventory     -> Erp.Shared.*   (added Phase 5; NOT -> Sales)
Erp.Shared.Infrastructure -> Erp.Shared.Messaging, Erp.Shared.Common
Erp.Shared.Messaging      -> Erp.Shared.Common
Erp.Shared.BuildingBlocks -> (nothing)
Erp.Shared.Common         -> (nothing)
```

No module references another module — the compiler enforces the island rule. `BuildingBlocks`/`Common`
reference nothing, so a Domain built on them can't drag in Dapper or the web. Those compile errors are
*features*.

## 2. Folder tree, with every folder defended

```
src/Shared/Erp.Shared.Messaging/
    IRequest.cs, IRequestHandler.cs, ISender.cs, IPipelineBehavior.cs   # mediator contract (Phase 2)

src/Shared/Erp.Shared.Infrastructure/
    Messaging/Sender.cs               # the hand-rolled dispatcher (Phase 2)
    Persistence/                      # IDbConnectionFactory, Dapper setup (Phase 4)
    DependencyInjection.cs            # AddSharedInfrastructure(): mediator + db + Carter services

src/Modules/Sales/
    Domain/                           # SalesOrder aggregate & rules (Phase 3). Uses only BuildingBlocks.
    Features/
        Ping/                         # walking-skeleton slice
            PingHandler.cs            #   PingQuery + PingHandler
            PingEndpoint.cs           #   Carter ICarterModule
    Infrastructure/                   # Dapper repositories, SQL (Phase 4)
    DependencyInjection.cs            # AddSalesModule(): registers this module's handlers/validators/repos

src/Erp.Api/
    Program.cs                        # composition root: AddSharedInfrastructure + AddSalesModule + MapCarter
    appsettings*.json
```

**Two placement rules:** (1) a use case's code lives in its feature folder as two files, *not* in generic
`Commands/`/`Handlers/`/`Endpoints/` folders; (2) anything shared by multiple use cases moves down — to the
module's `Domain/`/`Infrastructure/`, or to `Shared/` if every module needs it.

## 3. The walking-skeleton slice (Carter)

```csharp
// Modules/Sales/Features/Ping/PingHandler.cs
public sealed record PingQuery : IRequest<string>;
public sealed class PingHandler : IRequestHandler<PingQuery, string>
{
    public Task<string> Handle(PingQuery request, CancellationToken ct) => Task.FromResult("pong");
}

// Modules/Sales/Features/Ping/PingEndpoint.cs
public sealed class PingEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet("/sales/ping", async (ISender sender, CancellationToken ct) =>
            await sender.Send(new PingQuery(), ct));
}

// Erp.Api/Program.cs
builder.Services.AddSharedInfrastructure(builder.Configuration);
builder.Services.AddSalesModule(builder.Configuration);
var app = builder.Build();
app.MapCarter();
app.Run();
```

The mediator interfaces and `Add*` methods are fleshed out in Phase 2. **Preferred:** do Phase 1 and 2 back
to back so the skeleton runs through the real mediator. Otherwise mark any direct-call shim as throwaway.

## 4. Produce in this phase

- The four Shared projects + Sales module + Api + two test projects, with the references above.
- The folder tree (`.gitkeep` placeholders where empty).
- The `Ping` slice (two files); `AddSharedInfrastructure`/`AddSalesModule` stubs; `Program.cs` with
  `MapCarter()`.
- A repo-root `README` pointing at the phase docs.

**Explicitly NOT:** domain model, Dapper, stored procs, validation, business logic, a second module.

## 5. Pitfalls

- **Over-building the skeleton** (any DB access → not a skeleton).
- **Generic `Commands/`/`Handlers/`/`Endpoints/` folders** — the layered reflex that kills VSA.
- **Routes piled into `Program.cs`** — with Carter each feature owns its endpoint.
- **A module referencing another** (or a pure building block referencing the DB) — check the references.

---

## Checkpoint

1. Why split by module + Shared kernel instead of four layer projects?
2. Why do `BuildingBlocks`/`Common` reference nothing, and what breaks if a module references another?
3. What does Carter change about where endpoints live?
4. Why build a useless `Ping` slice before `CreateSalesOrder`?
5. Where does the shared `SalesOrder` aggregate live, and why not inside `CreateSalesOrder`?
