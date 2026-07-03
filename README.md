# Erp — Modular Monolith (Vertical Slice Architecture)

A **Sales** module for a fictional ERP, built as a teaching project for a **Modular Monolith** using
**Vertical Slice Architecture (VSA)** with a rich domain model, on .NET 10, Dapper + stored procedures,
Carter minimal APIs, FluentValidation, and a hand-rolled mediator.

The solution is organized **by business capability (module)**, and each module is organized **by
feature**, not by technical layer. There is no `Application` / `Domain` / `Infrastructure` layer split —
those are the traps this project deliberately avoids.

## Where to start

Three companion guides, same architecture, different depth:

- **[`plan-beginner-friendly/`](plan-beginner-friendly/)** — the hands-on build guide. Follow it phase by
  phase to build the solution. **Start here if you're building.**
- **[`plan-business-tutorial/`](plan-business-tutorial/)** — the "why" companion for a developer migrating
  from **ASP.NET WebForms (`.aspx.vb` + jQuery AJAX / `[WebMethod]`)**. Explains every decision by contrast
  with what you already know.
- **[`plan-Opus/`](plan-Opus/)** — the terse expert reference: decisions and reasoning, no hand-holding.

## Solution layout

```
Erp.slnx
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

## Running

```
dotnet run --project src/Erp.Api
```

`GET /sales/ping` should return `pong`.
