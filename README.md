# Erp — Modular Monolith

An Order module for a fictional ERP, built as a teaching project for Vertical Slice
Architecture + Clean Architecture + DDD on .NET 10, Dapper, and a hand-rolled mediator.

This project is being built phase by phase. Start at [`plan-Opus/00-README-overview.md`](plan-Opus/00-README-overview.md)
for the architecture decisions, then follow the phase files in order:

- [`plan-Opus/01-phase-0-orientation.md`](plan-Opus/01-phase-0-orientation.md)
- [`plan-Opus/02-phase-1-skeleton.md`](plan-Opus/02-phase-1-skeleton.md)
- [`plan-Opus/03-phase-2-mediator.md`](plan-Opus/03-phase-2-mediator.md)
- [`plan-Opus/04-phase-3-domain.md`](plan-Opus/04-phase-3-domain.md)
- [`plan-Opus/05-phase-4-create-order.md`](plan-Opus/05-phase-4-create-order.md)
- [`plan-Opus/06-phase-5-inventory-concurrency.md`](plan-Opus/06-phase-5-inventory-concurrency.md)
- [`plan-Opus/07-phase-6-queries.md`](plan-Opus/07-phase-6-queries.md)
- [`plan-Opus/08-phase-7-remaining-slices.md`](plan-Opus/08-phase-7-remaining-slices.md)
- [`plan-Opus/09-phase-8-testing.md`](plan-Opus/09-phase-8-testing.md)
- [`plan-Opus/10-phase-9-modularity.md`](plan-Opus/10-phase-9-modularity.md)

## Solution layout

```
Erp.sln
├── src/
│   ├── Erp.Domain/            entities, value objects, domain events. No dependencies.
│   ├── Erp.Application/       vertical feature slices, mediator abstractions, behaviors.
│   ├── Erp.Infrastructure/    Dapper, connection factory, stored-proc callers, dispatchers.
│   └── Erp.Api/               ASP.NET Core host, endpoints, DI composition root.
└── tests/
    ├── Erp.Domain.Tests/
    ├── Erp.Application.Tests/
    └── Erp.Integration.Tests/
```

## Running

```
dotnet run --project src/Erp.Api
```

`GET /orders/ping` should return `pong`.
