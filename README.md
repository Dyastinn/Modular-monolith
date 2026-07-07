# Erp — Modular Monolith (Vertical Slice Architecture)

A **Sales** module for a fictional ERP, built as a teaching project for a **Modular Monolith** using
**Vertical Slice Architecture (VSA)** with a rich domain model, on .NET 10, Dapper + stored procedures,
Carter minimal APIs, FluentValidation, and a hand-rolled mediator.

The solution is organized **by business capability (module)**, and each module is organized **by
feature**, not by technical layer. There is no `Application` / `Domain` / `Infrastructure` layer split —
those are the traps this project deliberately avoids.

## Where to start

**Start with [`plan-business-tutorial/`](plan-business-tutorial/) — this is the main course.** Two companion
guides teach the *same* architecture with supporting jobs. All three share phase numbers, so you can move
between them:

- **[`plan-business-tutorial/`](plan-business-tutorial/) — Build it for real (start here).** Roll up your
  sleeves and implement the full, secured Sales Order module end-to-end — JWT, roles, credit limits,
  inventory reservation, concurrency — applying the concepts **with your own hands.** It's **self-contained**:
  every step you need is in this folder, and it explains each concept as it builds it.
- **[`plan-beginner-friendly/`](plan-beginner-friendly/) — Understand it (optional companion).** A
  lightweight, concept-first, plain-English read: *what problem* each pattern solves and *how* the pieces
  fit, with analogies and answer keys. Perfect for a commute — you **mentally build** the system before (or
  alongside) writing any code. Reach for it when a concept in the tutorial feels shaky, or read it first if
  you like the *why* before the *how*. (Also written to help a developer coming from **ASP.NET WebForms**.)
- **[`plan-Opus/`](plan-Opus/) — Let AI build it.** A terse, unambiguous, **AI-ready spec.** Hand a phase to
  a coding agent (e.g. Claude Code) and have it implement exactly that phase — so you **see the architecture
  come together first-hand** and review real code against the plan.

> **Suggested path:** work through `plan-business-tutorial` end-to-end — that's the course. Keep
> `plan-beginner-friendly` open as a companion for the *why* behind anything that feels abstract, and use
> `plan-Opus` when you'd rather watch an AI implement a phase and review its code against the plan.

## Solution layout

```
Erp.slnx
Directory.Packages.props             one place for every NuGet version (Central Package Management)
src/
├── Shared/                          shared kernel — code every module may use
│   ├── Erp.Shared.BuildingBlocks/   Entity, AggregateRoot, ValueObject, IDomainEvent, DomainException
│   ├── Erp.Shared.Common/           Result/Error, IClock, guard helpers, paging — no dependencies
│   ├── Erp.Shared.Messaging/        hand-rolled mediator: IRequest, IRequestHandler, ISender, behaviors
│   └── Erp.Shared.Infrastructure/   Dapper connection factory, JWT/auth, error handling, Carter wiring
│
├── Modules/
│   └── Sales/  (Erp.Modules.Sales)  ONE module = ONE project = the isolation boundary
│       ├── Domain/                  SalesOrder aggregate, value objects, domain events. Depends on nothing but BuildingBlocks.
│       ├── Features/                one folder per business feature (the vertical slices)
│       │   ├── CreateSalesOrder/
│       │   │   ├── CreateSalesOrderHandler.cs    Command + Result + Validator + Handler
│       │   │   └── CreateSalesOrderEndpoint.cs   Request + Response + Carter endpoint
│       │   ├── SubmitSalesOrder/    ...
│       │   └── ...
│       ├── Infrastructure/          Dapper repositories, stored-proc callers, SQL scripts
│       └── DependencyInjection.cs   AddSalesModule() — the module's one public registration seam
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

- **`/swagger`** — Swagger UI with an **Authorize** button (paste the token from `POST /auth/login`).
- **`/health`** — returns `"healthy"`.

**Ready-made test interface (for the student):** the API is *not* coupled to any UI. Instead, a single
standalone file — [`course-validation/business/test-console/index.html`](course-validation/business/test-console/index.html)
— can be **opened straight from disk** (double-click) while the API runs. Log in and
create/submit/approve/cancel orders, watching each business rule fire, without writing a client. (The API
enables permissive CORS in Development so the file can call it.)

Seed logins (all password `Password123!`): **`rep`** (SalesRep), **`manager`** (SalesManager), **`admin`**
(Admin). Only a manager/admin can approve.

```
dotnet test        # 14 domain unit tests + 5 integration/e2e tests (uses a separate Erp_Test database)
```

> The three `plan-*` folders remain the *teaching* material; `course-validation/business/` is the finished
> business-tutorial code you can run and read alongside them.
