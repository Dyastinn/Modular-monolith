# Phase 1 — Build the Empty Frame First (the "Walking Skeleton")

> 📄 Expert version: [`../plan-Opus/02-phase-1-skeleton.md`](../plan-Opus/02-phase-1-skeleton.md)

**In one sentence:** Create the projects and folders for a modular monolith, then prove the whole pipe
works with one trivial feature — *before* adding any real complexity.

**What you'll build:** A solution that compiles, the full folder tree, and a `/sales/ping` feature that
goes web request → Carter endpoint → mediator → handler → responds `"pong"`. That's it.

**Before you start:** Phase 0 read. .NET 10 SDK installed. SQL Server reachable (LocalDB is fine).

---

## Why build something so useless first?

Imagine you assemble the database, the domain model, the mediator, and CQRS all at once — and it doesn't
work. **Which part is broken?** You'd be debugging five new things at once.

A **walking skeleton** is the thinnest possible slice that touches the *whole pipeline* but does almost
nothing. Once `GET /sales/ping` returns `"pong"` through the real machinery, you've *proven the pipe*.
Every real feature after this is just a fancier version of a path you already know works.

> 💡 **Analogy:** Before decorating a house, you frame it and confirm one lightbulb turns on. If the light
> works, the wiring and breaker panel are sound. Now you can add rooms confidently.

## 1. The projects (Shared kernel + one module + a host)

Unlike a Clean Architecture solution (four projects split by technical layer), a modular monolith is split
by **who owns the code**: shared building blocks everyone uses, one project per **module**, and a host
that starts it all.

```
Erp.slnx                              <- the "solution" (ties all projects together)
├── src/
│   ├── Shared/
│   │   ├── Erp.Shared.BuildingBlocks   <- base domain types (Entity, ValueObject…). Depends on NOTHING.
│   │   ├── Erp.Shared.Common           <- Result/Error, IClock, paging helpers. Depends on NOTHING.
│   │   ├── Erp.Shared.Messaging        <- the hand-rolled mediator's contracts (Phase 2)
│   │   └── Erp.Shared.Infrastructure   <- db connection factory, Carter/auth wiring
│   ├── Modules/
│   │   └── Sales/  (Erp.Modules.Sales) <- the Sales department — one module, one project
│   └── Erp.Api                         <- the host; wires every module together at startup
└── tests/
    ├── Erp.Modules.Sales.UnitTests     <- (used in Phase 8)
    └── Erp.Integration.Tests           <- (used in Phase 8)
```

> ❓ **"Why four Shared projects instead of one?"** Because they have different dependency rules, and
> keeping them separate is what *enforces* the "keep the domain pure" rule. `BuildingBlocks` and `Common`
> depend on *nothing*, so a module's Domain can use them freely. `Infrastructure` depends on Dapper and
> Carter — heavy stuff a Domain must never touch. If they were one project, the Domain could accidentally
> `using` a database type and you'd never notice. Separate projects make that a compile error. (If this
> feels like a lot for one module — it is; the payoff shows when the *second* module arrives in Phase 5.)

### How do I actually create all this? (the real commands)

Open a terminal in an empty folder and run these one at a time, using the `dotnet` command-line tool:

```bash
# 1) Create the solution file (--format slnx gives the newer XML "Erp.slnx"; omit it for a classic "Erp.sln")
dotnet new sln -n Erp --format slnx

# 2) Shared kernel — four small class libraries
dotnet new classlib -n Erp.Shared.BuildingBlocks  -o src/Shared/Erp.Shared.BuildingBlocks
dotnet new classlib -n Erp.Shared.Common          -o src/Shared/Erp.Shared.Common
dotnet new classlib -n Erp.Shared.Messaging       -o src/Shared/Erp.Shared.Messaging
dotnet new classlib -n Erp.Shared.Infrastructure  -o src/Shared/Erp.Shared.Infrastructure

# 3) The Sales module (one project) and the API host
dotnet new classlib -n Erp.Modules.Sales          -o src/Modules/Sales
dotnet new web      -n Erp.Api                     -o src/Erp.Api      # 'web' = a runnable web app

# 4) Test projects
dotnet new xunit -n Erp.Modules.Sales.UnitTests    -o tests/Erp.Modules.Sales.UnitTests
dotnet new xunit -n Erp.Integration.Tests          -o tests/Erp.Integration.Tests

# 5) Add every project to the solution
dotnet sln add (Get-ChildItem -Recurse *.csproj)   # PowerShell; on bash: dotnet sln add $(find . -name *.csproj)
```

- `classlib` = a "class library" — holds code but doesn't run on its own (all the Shared and module
  projects).
- `web` = a project that *can* run and listen for web requests (that's `Erp.Api`).
- `xunit` = a test project using the xUnit framework (Phase 8 uses these).

Now the important part — **which project may reference which.** You set these with `dotnet add reference`:

```
Erp.Api                → Erp.Modules.Sales, Erp.Shared.Infrastructure
Erp.Modules.Sales      → Erp.Shared.BuildingBlocks, Erp.Shared.Common,
                         Erp.Shared.Messaging, Erp.Shared.Infrastructure
Erp.Shared.Infrastructure → Erp.Shared.Messaging, Erp.Shared.Common
Erp.Shared.Messaging      → Erp.Shared.Common
Erp.Shared.BuildingBlocks → (nothing!)
Erp.Shared.Common         → (nothing!)
```

For example, to say "the Sales module may use the mediator contracts," you'd run:

```bash
dotnet add src/Modules/Sales/Erp.Modules.Sales.csproj reference src/Shared/Erp.Shared.Messaging/Erp.Shared.Messaging.csproj
```

> ❓ **"What is a 'reference'?"** A reference is one project saying "I'm allowed to use the code in that
> other project." Without a reference, the compiler pretends the other project doesn't exist. This is how
> we *enforce* the two rules from Phase 0:
> - **Modules are islands:** we simply never give `Erp.Modules.Sales` a reference to another module. So
>   Sales *can't* use Inventory's code, even by accident — it won't compile. (When Inventory arrives in
>   Phase 5, it also won't reference Sales.)
> - **Keep the Domain pure:** `BuildingBlocks` and `Common` reference nothing, so the pure base types a
>   Domain builds on can never drag in Dapper or the web.

> 🛡️ **These walls are a safety net, not red tape.** The moment someone tries to make Sales reach into
> Inventory, or make a pure building block depend on the database, the project *won't compile*. That
> compile error is a **feature** — the computer enforces the boundary so a tired human doesn't have to.

## 2. The folder tree, with every folder justified

Here's the target layout inside the Sales module and the Api. For each folder, ask: *why does it exist,
and why does code live here and not somewhere else?*

```
src/Shared/Erp.Shared.Messaging/
    IRequest.cs, IRequestHandler.cs, ISender.cs, IPipelineBehavior.cs   # the mediator's contracts (Phase 2)

src/Shared/Erp.Shared.Infrastructure/
    Messaging/Sender.cs               # the hand-built mediator's guts (Phase 2)
    Persistence/                      # db connection factory, stored-proc helpers (Phase 4)
    DependencyInjection.cs            # AddSharedInfrastructure(): mediator + db + Carter services

src/Modules/Sales/
    Domain/                           # the SalesOrder aggregate & rules (Phase 3). Uses only BuildingBlocks.
    Features/
        Ping/                         # ← our walking-skeleton feature
            PingHandler.cs            #   PingQuery + PingHandler (one file)
            PingEndpoint.cs           #   the Carter endpoint (one file)
        CreateSalesOrder/             # (Phase 4)
        ...
    Infrastructure/                   # this module's Dapper repositories & SQL (Phase 4)
    DependencyInjection.cs            # AddSalesModule(): registers this module's handlers, validators, repos

src/Erp.Api/
    Program.cs                        # the "composition root": wires up every module at startup
    appsettings.json
    appsettings.Development.json
```

**Two placement rules to burn into memory now:**

1. **A feature's code lives in its feature folder** (`Sales/Features/CreateSalesOrder/`), split into just
   two files (handler + endpoint) — **not** spread across generic `Commands/`, `Validators/`, `Handlers/`,
   `Endpoints/` folders. Grouping by technical *type* is the old layered anti-pattern VSA rejects.
2. **Anything shared by many features moves *down*** — to the module's `Domain/` if it's a business object
   (the `SalesOrder`), to the module's `Infrastructure/` if it's plumbing, or to `Shared/` if *every*
   module needs it (the mediator). The `SalesOrder` object is used by many features, so it lives in
   `Sales/Domain/`, *not* inside the `CreateSalesOrder` folder.

## 3. The `/sales/ping` feature (the whole skeleton)

`PingQuery` is a request that returns a `string`. `PingHandler` just returns `"pong"`. No database, no
business logic. Its *only* job: prove **Carter endpoint → mediator finds the right handler → response
comes back.**

```csharp
// Modules/Sales/Features/Ping/PingHandler.cs
namespace Erp.Modules.Sales.Features.Ping;

public sealed record PingQuery : IRequest<string>;

public sealed class PingHandler : IRequestHandler<PingQuery, string>
{
    public Task<string> Handle(PingQuery request, CancellationToken ct)
        => Task.FromResult("pong");
}
```

```csharp
// Modules/Sales/Features/Ping/PingEndpoint.cs
namespace Erp.Modules.Sales.Features.Ping;

public sealed class PingEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet("/sales/ping",
            async (ISender sender, CancellationToken ct)
                => await sender.Send(new PingQuery(), ct));
}
```

```csharp
// Erp.Api/Program.cs (the composition root)
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSharedInfrastructure(builder.Configuration);  // mediator, db, Carter services
builder.Services.AddSalesModule(builder.Configuration);           // the Sales module plugs itself in

var app = builder.Build();
app.MapCarter();   // Carter finds every ICarterModule and maps its routes
app.Run();
```

### Let's read that code

Don't let the symbols scare you — every piece, in plain English:

**`public sealed record PingQuery : IRequest<string>;`**
- `record` = a small data-holder type (see [Foundations §7](00a-FOUNDATIONS.md)). `PingQuery` carries no
  data — an empty request, like an envelope with nothing inside.
- `: IRequest<string>` = "this is a request that, when handled, produces a `string`." (For `/ping`, the
  answer is `"pong"`.)

**`public sealed class PingHandler : IRequestHandler<PingQuery, string>`**
- The *worker* for `PingQuery`. `IRequestHandler<PingQuery, string>` reads as "I handle a `PingQuery` and
  return a `string`."
- `=> Task.FromResult("pong")` — since there's no real waiting (no database), we hand back the answer
  immediately. Real handlers will `await` actual work.

**`public sealed class PingEndpoint : ICarterModule`**
- This is **Carter**. Instead of piling every route into `Program.cs`, each feature declares its own
  endpoint as a tiny `ICarterModule` living *right next to its handler*. That's VSA carried all the way to
  the web edge.
- `AddRoutes` is Carter's one method: "here are my routes." Inside, `app.MapGet("/sales/ping", …)` says
  "when a browser GETs `/sales/ping`, run this."
- `ISender sender` = the mediator (post office) gets *handed to us* here (dependency injection again).
- `await sender.Send(new PingQuery(), ct)` = "post office, please handle a `PingQuery`, and wait for the
  answer." The endpoint doesn't know *which* handler runs — that's the whole point of the mediator.

**`app.MapCarter();`** in `Program.cs` — one line that tells Carter to scan the loaded modules, find every
`ICarterModule` (like `PingEndpoint`), and wire up its routes. You never edit a central route list again;
you just add a feature folder.

So the full journey is: **browser → `PingEndpoint` → `sender.Send` → mediator finds `PingHandler` →
returns `"pong"` → back out to the browser.** Tiny, but it's the *entire* shape every real feature follows.

> ⚠️ **The mediator interfaces (`IRequest`, `IRequestHandler`, `ISender`) and the `AddSharedInfrastructure`
> / `AddSalesModule` methods don't fully exist yet** — you build them in Phase 2. **Recommended:** do
> Phase 1 and Phase 2 back-to-back so `/sales/ping` runs through the real mediator immediately. If you must
> pause, you can temporarily call `PingHandler` directly — but mark that clearly as throwaway.

> ❓ **"Why is a query called `PingQuery` and not `PingCommand`?"** Because it only *reads* / returns
> information — it doesn't *change* anything. Requests that change state are commands
> (`CreateSalesOrderCommand`); requests that just read are queries (`GetSalesOrderQuery`). You'll see this
> split matter a lot in Phase 6.

## 4. What to produce in this phase

- The four Shared projects + the Sales module + the Api + two test projects, with the reference directions
  from §1.
- The folder tree (empty folders can hold a `.gitkeep` placeholder).
- The `Ping` query + handler and the `PingEndpoint` Carter module.
- `AddSharedInfrastructure` and `AddSalesModule` stubs, and `Program.cs` wired so the app starts.
- A short `README` at the repo root pointing at these phase docs.

**Explicitly NOT now:** no domain model, no database, no stored procedures, no validation, no real
business logic, no second module. If you feel the urge to add any of it — don't. That urge is exactly what
this phase trains you to resist.

### How to run it and confirm it works

From the repo root:

```bash
dotnet run --project src/Erp.Api
```

You'll see `Now listening on: http://localhost:5xxx`. Open that address with `/sales/ping` on the end, or
from another terminal:

```bash
curl http://localhost:5123/sales/ping
```

If you see `pong` — **congratulations, your walking skeleton walks.** That single word travelled through
Carter, the mediator, and the handler, and back. Every feature you build after this is a bigger version of
that exact round trip.

> ❓ **"It returned `pong` but I don't feel like I did much."** Correct and intended. The skeleton is
> *supposed* to be almost nothing. Its value isn't what it does — it's that it *proves the wiring*. Now
> when Phase 4's create-order feature doesn't work, you'll know the problem is in *your new code*, not the
> plumbing, because the plumbing already returned `pong`.

## 5. Common mistakes to avoid

- **Over-building the skeleton.** If `/ping` touches a database, it's not a skeleton anymore. Keep it dumb.
- **Making generic `Commands/`, `Handlers/`, or `Endpoints/` folders.** This is the #1 reflex from old
  tutorials and it quietly destroys the whole benefit of VSA. Group by *feature*, always.
- **Piling routes into `Program.cs`.** With Carter, each feature owns its endpoint. `Program.cs` just
  calls `app.MapCarter()`.
- **Giving one module a reference to another** (or letting a pure building block reference the database).
  If it compiles when it shouldn't, you've broken a wall — so actually check the references.

---

## ✅ Check Yourself

1. **Why is the solution split by *module* (Sales) plus a *Shared* kernel, instead of four Clean
   Architecture layer projects?**
2. **Why do `BuildingBlocks` and `Common` reference nothing, and what helpfully breaks if a module tries
   to reference another module?**
3. **What does Carter change about where endpoints live, versus piling `app.MapGet` calls into
   `Program.cs`?**
4. **Why build a `/sales/ping` feature that does nothing useful before building `CreateSalesOrder`?**
5. **Where does the shared `SalesOrder` object live, and why not inside the `CreateSalesOrder` folder?**

---

## 👨‍🏫 Answer Key

**1. Why split by module + Shared kernel instead of four layer projects?**
Because we organize by *business capability*, not technical layer. A module (Sales) owns one department's
whole vertical stack; the Shared kernel holds what *every* module needs (mediator, base types, db
plumbing). This keeps a feature's code together and makes module walls real. The four-layer split scatters
one feature across four projects and provides no departmental boundary at all.

**2. Why do `BuildingBlocks`/`Common` reference nothing, and what breaks if a module references another?**
They reference nothing so the pure base types can never drag in Dapper or the web — keeping any Domain
built on them instantly testable. If `Sales` tried to reference `Inventory`, ideally you simply *never add
that reference*, so it **won't compile** if someone writes `using Erp.Modules.Inventory…`. That compile
error is the island wall doing its job.

**3. What does Carter change about endpoints?**
Carter lets each feature declare its own endpoint as a small `ICarterModule` living next to its handler,
and `app.MapCarter()` discovers them all. So routes are organized by *feature* (VSA to the web edge)
instead of piled into a central `Program.cs` list that every feature has to edit.

**4. Why build a useless `/sales/ping` first?**
To **prove the plumbing before adding complexity**. If you wired the database, domain, mediator, and
validation all at once and it failed, you couldn't tell which part broke. `/ping` is the "one lightbulb"
test: once a web request reaches a handler and returns through the real machinery, the pipe is sound, and
every real feature is a fancier version of that proven path.

**5. Where does the shared `SalesOrder` live, and why not in `CreateSalesOrder`?**
In the module's `Sales/Domain/` folder. It does **not** go in `CreateSalesOrder` because it's shared by
*many* features — create, submit, cancel all use the same `SalesOrder`. The rule: *anything used by
multiple features moves down into a shared spot* (the module's Domain). Trapping it in one feature's folder
would force the others to reach across into that folder — a tangle.

> ✅ **You're ready for Phase 2 if:** your solution compiles, `GET /sales/ping` returns something, and you
> can explain why the folders are grouped by module and feature. The *running skeleton* matters more than
> understanding every line yet — the understanding fills in as you build real features.
