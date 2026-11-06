# Erp — a Modular Monolith you build, then run

A course that ends in a working ERP for a small business: sales orders with credit checks and approvals, a
warehouse with an audited item catalog and stock, purchasing, invoicing with gap-free numbers and PDFs,
payments and receivables aging, and staff accounts, all with their screens. It's built as a **modular
monolith** with **vertical slices** on .NET 10 and PostgreSQL. The reference build *is* the product: it
installs with Docker Compose (or on IIS) and runs.

| | |
|---|---|
| Architecture | Modular monolith; each module is `internal` behind one public Contracts project; vertical slices inside |
| Modules | Identity, Sales, Inventory, Purchasing, Invoicing |
| Stack | .NET 10, Carter, `martinothamar/Mediator`, FluentValidation, EF Core (writes), Dapper + SQL functions (reads), PostgreSQL |
| Screens | Blazor WebAssembly + MudBlazor, served by the API itself; the API lives under `/api` |
| Sign-in | ASP.NET Core Identity: cookies for the browser, bearer and refresh tokens for programs, lockout, six roles, admin-managed staff accounts |
| Production | Transactional outbox, OpenTelemetry, health checks, rate limits, HTTPS/HSTS, a Content-Security-Policy, certificate-encrypted sign-in keys, least-privilege database roles |
| Deployment | Docker Compose with Caddy (HTTPS) and nightly backups; IIS as the alternative |
| Tests | xUnit + AwesomeAssertions: domain unit tests, integration tests against PostgreSQL, architecture tests, Playwright browser tests |
| Licenses | Permissive open source only; see [THIRD-PARTY-NOTICES.md](course-validation/business/THIRD-PARTY-NOTICES.md) |

## Where to start

**[plan-business-tutorial/](plan-business-tutorial/) is the course.** Open
[00-START-HERE.md](plan-business-tutorial/00-START-HERE.md) and work through lessons 01 to 24 in order. The
interface is built along with the logic: from lesson 01 the app opens in your browser, and every lesson that
adds a feature adds its screen, so each lesson ends in something you click through.

| Reference page | What it's for |
|---|---|
| [00a-FOUNDATIONS.md](plan-business-tutorial/00a-FOUNDATIONS.md) | The C# the course leans on: interfaces, DI, `async`, generics, records |
| [00b-WINDOWS-POWERSHELL.md](plan-business-tutorial/00b-WINDOWS-POWERSHELL.md) | Every terminal checkpoint translated into PowerShell |
| [GLOSSARY.md](plan-business-tutorial/GLOSSARY.md) | Every term, in plain English, with the lesson that introduces it |
| [ANSWER-KEY.md](plan-business-tutorial/ANSWER-KEY.md) | Model answers to every lesson's "Let's check!" questions |
| [LEARNING-RESOURCES.md](plan-business-tutorial/LEARNING-RESOURCES.md) | Where to go deeper, and how to get unstuck |

The lessons are written for someone coming from ASP.NET WebForms, too: where an idea has a WebForms
equivalent, the lesson says so.

For contributors: [CLAUDE.md](CLAUDE.md) holds the rules, [WRITING-STYLE.md](WRITING-STYLE.md) the lesson
voice, [ROADMAP.md](ROADMAP.md) the plan and progress, and
[course-validation/FINDINGS.md](course-validation/FINDINGS.md) what was verified and what wasn't.

## Solution layout

```
Erp.slnx
Directory.Build.props, Directory.Packages.props   shared settings; every package version, once
Dockerfile, .dockerignore                         the app and migrate images
src/
  Erp.Api/                          the host: Program.cs, error handling, security, observability; serves Erp.Web
  Erp.Web/                          the Blazor WebAssembly app: pages, layout, ApiClient
  Erp.AppHost/                      development only: runs the API next to the Aspire dashboard
  Shared/
    Erp.Shared.BuildingBlocks/      what every module may use: base classes, Money, Quantity, roles, small interfaces
    Erp.Shared.Infrastructure/      implementations: connections, outbox, telemetry. Only the host sees it
  Modules/
    Identity/Erp.Modules.Identity/  sign-in (ASP.NET Core Identity), staff accounts, sign-in log, email
    Sales/Erp.Modules.Sales/        customers, price list, sales orders and their approval
      Domain/                       SalesOrder, Customer, SalesItem, ...: the business rules, no database
      Features/                     one folder per feature: XxxHandler.cs + XxxEndpoint.cs
      Infrastructure/               SalesDbContext, migrations, SQL functions, event handlers
      SalesModule.cs                the module's one public type
    Sales/Erp.Modules.Sales.Contracts/            the only part other modules may reference
    Inventory/Erp.Modules.Inventory[.Contracts]   item catalog, stock, adjustments, shipments
    Purchasing/Erp.Modules.Purchasing             suppliers, purchase orders, receiving
    Invoicing/Erp.Modules.Invoicing[.Contracts]   invoices, PDFs, payments, receivables aging
tests/
  Erp.Modules.<Module>.UnitTests/   each module's rules, no database
  Erp.Integration.Tests/            the whole API against a real PostgreSQL database
  Erp.Architecture.Tests/           fail the build when a module breaks a boundary
  Erp.Web.UiTests/                  Playwright, against a running app
deploy/postgres/create-roles.sql    the two production database accounts
deploy/docker/                      compose.yaml, .env.example, Caddyfile, migrate and backup scripts
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
data on start. Open **http://localhost:5264** and sign in. The seeded users all have the password
`Password123!`:

| Email | Role |
|---|---|
| `rep@erp.local` | SalesRep |
| `manager@erp.local` | SalesManager |
| `admin@erp.local` | Admin |
| `clerk@erp.local` | InventoryClerk |
| `buyer@erp.local` | Purchaser |
| `accountant@erp.local` | Accountant |

Also there: **`/swagger`** (the API, clickable; the sign-in cookie works there too), and **`/health/live`**
and **`/health/ready`**.

To see logs, traces and metrics while you work, run the Aspire AppHost instead. It needs no Docker:

```bash
dotnet run --project src/Erp.AppHost
```

And the tests, which use their own `erp_test` database:

```bash
dotnet test     # 45 unit + 7 architecture + 35 integration; the 3 browser tests skip without ERP_UI_URL
ERP_UI_URL=http://localhost:5264 dotnet test tests/Erp.Web.UiTests     # with the app running
```

The reference build is there to check your work, not to replace it. Build it yourself first, by following
the lessons. To install it on a server, follow [lesson 22](plan-business-tutorial/22-docker.md) (Docker) or
[lesson 23](plan-business-tutorial/23-deploy-iis.md) (IIS), then the first-day steps in
[lesson 24](plan-business-tutorial/24-production-checklist.md).

## Status

All five modules and their screens are built and tested (ROADMAP M1–M7). Two install paths are written but
haven't been executed end to end on this machine: the Docker install (Docker wasn't available) and IIS on a
real Windows Server. Before issuing official invoices, a business must confirm the BIR's requirements with a
tax adviser. [Lesson 24](plan-business-tutorial/24-production-checklist.md) lists what isn't built yet.
