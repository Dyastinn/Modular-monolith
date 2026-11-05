# Erp — a Modular Monolith you build, then run

A course that ends in a working ERP for a small business: sales orders with credit checks and approvals,
stock reservations, sign-in and roles, all built as a **modular monolith** with **vertical slices** on
.NET 10 and PostgreSQL. The reference build *is* the product: it deploys to IIS and runs.

| | |
|---|---|
| Architecture | Modular monolith; each module is `internal` behind one public Contracts project; vertical slices inside |
| Stack | .NET 10, Carter, `martinothamar/Mediator`, FluentValidation, EF Core (writes), Dapper + SQL functions (reads), PostgreSQL |
| Sign-in | ASP.NET Core Identity: cookies for browsers, bearer and refresh tokens for apps, lockout, roles |
| Production | Transactional outbox, OpenTelemetry, health checks, rate limits, HTTPS/HSTS, least-privilege database roles |
| Tests | xUnit + AwesomeAssertions: domain unit tests, integration tests against PostgreSQL, architecture tests |
| Licenses | Permissive open source only; see [THIRD-PARTY-NOTICES.md](course-validation/business/THIRD-PARTY-NOTICES.md) |

## Where to start

**[plan-business-tutorial/](plan-business-tutorial/) is the course.** Open
[00-START-HERE.md](plan-business-tutorial/00-START-HERE.md) and work through lessons 01 to 16 in order. Each
one ends in something you can run and check.

| Reference page | What it's for |
|---|---|
| [00a-FOUNDATIONS.md](plan-business-tutorial/00a-FOUNDATIONS.md) | The C# the course leans on: interfaces, DI, `async`, generics, records |
| [00b-WINDOWS-POWERSHELL.md](plan-business-tutorial/00b-WINDOWS-POWERSHELL.md) | Every checkpoint translated into PowerShell |
| [GLOSSARY.md](plan-business-tutorial/GLOSSARY.md) | Every term, in plain English, with the lesson that introduces it |
| [ANSWER-KEY.md](plan-business-tutorial/ANSWER-KEY.md) | Model answers to every lesson's "Let's check!" questions |
| [LEARNING-RESOURCES.md](plan-business-tutorial/LEARNING-RESOURCES.md) | Where to go deeper, and how to get unstuck |

The lessons are written for someone coming from ASP.NET WebForms, too: where an idea has a WebForms
equivalent, the lesson says so.

For contributors: [CLAUDE.md](CLAUDE.md) holds the rules, [WRITING-STYLE.md](WRITING-STYLE.md) the lesson
voice, and [ROADMAP.md](ROADMAP.md) the plan and progress.

## Solution layout

```
Erp.slnx
Directory.Build.props, Directory.Packages.props   shared settings; every package version, once
src/
  Erp.Api/                          the host: Program.cs, error handling, security, observability
  Erp.AppHost/                      development only: runs the API next to the Aspire dashboard
  Shared/
    Erp.Shared.BuildingBlocks/      what every module may use: base classes, small interfaces, roles
    Erp.Shared.Infrastructure/      implementations: connections, outbox, telemetry. Only the host sees it
  Modules/
    Identity/Erp.Modules.Identity/  sign-in (ASP.NET Core Identity), /auth/me, sign out everywhere
    Sales/Erp.Modules.Sales/
      Domain/                       SalesOrder, Customer, Money, ...: the business rules, no database
      Features/                     one folder per feature: XxxHandler.cs + XxxEndpoint.cs
      Infrastructure/               SalesDbContext, migrations, SQL functions, event handlers
      SalesModule.cs                the module's one public type
    Inventory/
      Erp.Modules.Inventory/        stock levels and reservations
      Erp.Modules.Inventory.Contracts/   the only part other modules may reference
tests/
  Erp.Modules.Sales.UnitTests/      the domain's rules, no database
  Erp.Integration.Tests/            the whole API against a real PostgreSQL database
  Erp.Architecture.Tests/           fail the build when a module breaks a boundary
deploy/postgres/create-roles.sql    the two production database accounts
```

Three rules hold it together:

1. **Modules are islands with one service window.** Everything in a module is `internal` except its entry
   class. Another module may reference only its `.Contracts` project, never the module itself.
2. **No SQL crosses a schema.** Each module owns its schema and its DbContext. Cross-module work goes
   through a contract or an event.
3. **The domain is pure.** `Domain/` depends on nothing but BuildingBlocks, so every rule is unit-tested
   without a database.

## Running the reference build

You need the .NET 10 SDK and PostgreSQL 16 or later. Lesson 04 installs PostgreSQL with `winget`; the
development connection string expects the `postgres` user with password `erp_dev_only` on `localhost:5432`.

```bash
cd course-validation/business
dotnet run --project src/Erp.Api
```

In Development, the app creates the `erp` database, applies every module's migrations and seeds sample
data on start. It listens on **http://localhost:5264**:

- **`/swagger`**: the API, clickable. **Authorize** asks for two things: the `web` API key from
  [appsettings.Development.json](course-validation/business/src/Erp.Api/appsettings.Development.json), and
  the `accessToken` that `POST /auth/login` returns.
- **`/health/live`** and **`/health/ready`**: is the process up, and can it reach the database?
- **[test-console/index.html](course-validation/business/test-console/index.html)**: open it straight from
  disk while the API runs, to sign in and create, submit, approve, reject and cancel orders by clicking.

Every path except `/health` and `/swagger` needs the `X-Api-Key` header, sign-in included. A request without
it gets a 401 that looks like a broken login until you know to look for the key.

The seeded users all have the password `Password123!`: `rep@erp.local` (SalesRep),
`manager@erp.local` (SalesManager) and `admin@erp.local` (Admin).

To see logs, traces and metrics while you work, run the Aspire AppHost instead. It needs no Docker:

```bash
dotnet run --project src/Erp.AppHost
```

And the tests, which use their own `erp_test` database:

```bash
dotnet test     # 22 unit + 6 architecture + 25 integration
```

The reference build is there to check your work, not to replace it. Build it yourself first, by following
the lessons. To deploy it to a real server, follow
[lesson 24](plan-business-tutorial/24-deploy-iis.md).

## Status

The Sales and Inventory core, sign-in and the production plumbing are done. Customer and item maintenance,
user management, stock movements, the Blazor front end, Purchasing and Invoicing are next; the
[roadmap](ROADMAP.md) has the full list and order.
