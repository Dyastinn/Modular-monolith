# Erp — Modular Monolith (Vertical Slice Architecture)

A **Sales** module for a fictional ERP, built as a teaching project for a **Modular Monolith** using
**Vertical Slice Architecture (VSA)** with a rich domain model, on .NET 10, Dapper + stored procedures,
Carter minimal APIs, FluentValidation, and a hand-rolled mediator.

The solution is organized **by business capability (module)**, and each module is organized **by
feature**, not by technical layer. There is no `Application` / `Domain` / `Infrastructure` layer split —
those are the traps this project deliberately avoids.

## Where to start

**[`plan-business-tutorial/`](plan-business-tutorial/) is the course.** Open
[00-START-HERE.md](plan-business-tutorial/00-START-HERE.md) and work through the phases in order.

You build the full, secured Sales Order module end-to-end — JWT, roles, credit limits, inventory
reservation, concurrency — with your own hands. Every step you need is in that folder, and every concept is
explained where it's first used, starting from the problem it solves rather than the solution. Nothing is
assumed about what you already know, and no phase sends you elsewhere to fill a gap.

Five reference files sit alongside the phases:

| File | What it's for |
|---|---|
| [00a-FOUNDATIONS.md](plan-business-tutorial/00a-FOUNDATIONS.md) | The C# features the course leans on (records, `async`/`await`, interfaces, generics). Read it first if C# itself is new. |
| [00b-WINDOWS-POWERSHELL.md](plan-business-tutorial/00b-WINDOWS-POWERSHELL.md) | Every checkpoint translated into PowerShell, for Windows readers who'd rather not use Git Bash. |
| [GLOSSARY.md](plan-business-tutorial/GLOSSARY.md) | Every term the course uses, in plain English. |
| [ANSWER-KEY.md](plan-business-tutorial/ANSWER-KEY.md) | Model answers to every phase's **Check Yourself** questions; doubles as an instructor's discussion guide. |
| [LEARNING-RESOURCES.md](plan-business-tutorial/LEARNING-RESOURCES.md) | Where to go deeper, and how to get unstuck. |

The tutorial is also written for someone coming from **ASP.NET WebForms** — 🧭 markers throughout translate
each new idea back to `.aspx.vb`, `[WebMethod]`, and `SqlCommand`.

## Solution layout

```
Erp.slnx
Directory.Packages.props             one place for every NuGet version (Central Package Management)
src/
├── Shared/                          two projects: what a module may see, and what it may not
│   ├── Erp.Shared.BuildingBlocks/   everything a module may use — depends on nothing
│   │   ├── Domain/                  Entity, AggregateRoot, IDomainEvent, DomainException
│   │   ├── Abstractions/            IClock, IDbConnectionFactory, ICurrentUser, PagedResult, cross-module contracts
│   │   └── Messaging/               hand-rolled mediator: IRequest, IRequestHandler, ISender, behaviors
│   └── Erp.Shared.Infrastructure/   Dapper connection factory, JWT/auth, refresh tokens, Carter wiring
│                                    NO module references this — that missing reference is the wall
│
├── Modules/
│   ├── Sales/  (Erp.Modules.Sales)  ONE module = ONE project = the isolation boundary
│   │   ├── Domain/                  SalesOrder aggregate, value objects, domain events. Depends on nothing but BuildingBlocks.
│   │   ├── Features/                one folder per business feature (the vertical slices)
│   │   │   ├── CreateSalesOrder/
│   │   │   │   ├── CreateSalesOrderHandler.cs    Command + Result + Validator + Handler
│   │   │   │   └── CreateSalesOrderEndpoint.cs   Request + Response + Carter endpoint
│   │   │   ├── SubmitSalesOrder/    ...
│   │   │   └── ...
│   │   ├── Infrastructure/          Dapper repositories, stored-proc callers, SQL scripts
│   │   └── DependencyInjection.cs   AddSalesModule() — the module's one public registration seam
│   │
│   └── Inventory/ (Erp.Modules.Inventory)  reserves/releases stock; Sales reaches it only through
│                                           the IStockReservation contract published in Shared.BuildingBlocks
│
└── Erp.Api/                         host / composition root: wires modules, Carter, auth, error handling

tests/
├── Erp.Modules.Sales.UnitTests/     pure domain + handler tests (fakes, no DB)
└── Erp.Integration.Tests/           end-to-end through the real HTTP + SQL pipeline
```

**The two rules that replace Clean Architecture's concentric layers:**

1. **Modules are islands.** `Sales` never references `Inventory`'s internals (or vice versa). Cross-module
   work goes through a published contract in `Shared`, never a direct `using`. This is what keeps the
   "extract a module into its own service later" option cheap.
2. **Inside a module, keep the Domain pure.** `Features/` and `Infrastructure/` use `Domain/`; `Domain/`
   references only `Shared.BuildingBlocks`. Same testability payoff as Clean Architecture's inner circle,
   without four top-level projects.

## Feature file convention

Each feature is **two files**, so a developer understands the whole slice by opening one or two files:

- `XxxHandler.cs` — the **Command/Query**, its **Result**, its **Validator**, and its **Handler**.
- `XxxEndpoint.cs` — the **Request** (when it differs from the command), the **Response**, and the
  **Carter endpoint**.

Do not split every type into its own file. Keep the slice together.

## Running the reference build

A complete, working reference implementation of the business tutorial lives in
[`course-validation/business/`](course-validation/business/). It targets **.NET 10** and **SQL Server
LocalDB** (`(localdb)\MSSQLLocalDB`).

```
cd course-validation/business
dotnet run --project src/Erp.Api
```

On first start the app **creates the `Erp` database and applies each module's `Infrastructure/Sql/`
scripts automatically** (a small `DatabaseInitializer` runs the embedded scripts — the "migrations without
EF" approach), so there is nothing to set up by hand. The API listens on **http://localhost:5264**:

- **`/swagger`** — Swagger UI. Its **Authorize** button asks for **two** things: an `X-Api-Key` (which client
  app) and a bearer token (which user). Paste the dev `web` key from
  [`appsettings.Development.json`](course-validation/business/src/Erp.Api/appsettings.Development.json), then
  call `POST /auth/login` and paste the access token it returns.
- **`/health`** — returns `"healthy"`.

> Every path except `/health` and `/swagger` is gated by the **`X-Api-Key` header** (Phase 4 §11) — including
> the anonymous `/auth/login`. A `curl` without it gets `401 {"error":"Missing or invalid API key."}`, which
> looks like a broken login until you know to look for it.

**Ready-made test interface (for the student):** the API is *not* coupled to any UI. Instead, a single
standalone file — [`course-validation/business/test-console/index.html`](course-validation/business/test-console/index.html)
— can be **opened straight from disk** (double-click) while the API runs. Log in and
create/submit/approve/cancel orders, watching each business rule fire, without writing a client. (The API
enables permissive CORS in Development so the file can call it.)

Seed logins (all password `Password123!`): **`rep`** (SalesRep), **`manager`** (SalesManager), **`admin`**
(Admin). Only a manager/admin can approve.

```
dotnet test        # 14 domain unit tests + 7 integration/e2e tests (uses a separate Erp_Test database)
```

> [`plan-business-tutorial/`](plan-business-tutorial/) is the *teaching* material;
> `course-validation/business/` is the finished code you can run and read alongside it. Build it yourself
> first — the reference build is there to check your work, not to replace the typing.

> An **optional** second reference build,
> [`course-validation/business-phase15/`](course-validation/business-phase15/), applies the optional refactors
> on top of the base build: per-module contracts + `internal`
> ([Phase 15](plan-business-tutorial/15-scaling-the-wall.md)), then a module shrunk to a single public type
> ([Phase 15b](plan-business-tutorial/15b-internal-all-the-way.md)). One build backs both phases so there's
> only one copy to keep in sync; the base build above is what Phases 0–14 describe.
