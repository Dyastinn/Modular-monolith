# Erp — a Modular Monolith you build, then run

A course that ends in a working ERP for a small business: sales orders with credit checks and approvals, a
warehouse with an audited item catalog and stock, purchasing, invoicing with gap-free numbers and PDFs laid
out to the BIR's RR 7-2024, payments and receivables aging, and staff accounts, all with their screens. It's
built as a **modular monolith** with **vertical slices** on .NET 10 and PostgreSQL. The reference build *is*
the product: it installs with Compose on Podman (or Docker) and runs.

| | |
|---|---|
| Architecture | Modular monolith; each module is `internal` behind one public Contracts project; vertical slices inside |
| Modules | Identity, Sales, Inventory, Purchasing, Invoicing |
| Stack | .NET 10 minimal APIs (`TypedResults`, no mediator: endpoints call their handler), FluentValidation, EF Core (writes), Dapper + SQL functions (reads), PostgreSQL |
| Screens | Blazor Web App + MudBlazor running as WebAssembly, served by the API itself; the API lives under `/api` |
| Errors | Every error carries a stable code (`SALES-0003`) and a trace id; the app's **Error codes** page explains each code and its fix |
| Sign-in | ASP.NET Core Identity: cookies for the browser, bearer and refresh tokens for programs, lockout, six roles, admin-managed staff accounts |
| Production | Transactional outbox, OpenTelemetry, health checks, rate limits, HTTPS/HSTS, a Content-Security-Policy, certificate-encrypted sign-in keys, least-privilege database roles |
| Invoicing and tax | The company's registered details, tax regime, VAT rate and BIR permit kept on a screen; VATable, zero-rated and VAT-exempt items; senior citizen and PWD discounts; tax withheld and BIR Form 2307 tracking; the BIR's required fields, REPRINT marking and permit footer on every PDF |
| Deployment | Compose on rootless Podman (Docker runs the same files), Caddy for HTTPS, nightly backups, on any Linux server |
| Tests | Taught from lesson 02, in every lesson, edge cases included: xUnit v3 + AwesomeAssertions unit tests, FsCheck property tests, integration tests on a throwaway PostgreSQL (Testcontainers) run as the least-privilege role, architecture tests, race tests, Playwright browser tests; coverlet coverage and Stryker.NET mutation testing; GitHub Actions CI |
| Licenses | Permissive open source only; see [THIRD-PARTY-NOTICES.md](course-validation/business/THIRD-PARTY-NOTICES.md) |

## Where to start

**[plan-business-tutorial/](plan-business-tutorial/) is the course.** Open
[00-START-HERE.md](plan-business-tutorial/00-START-HERE.md) and work through lessons 01 to 25 in order. The
interface is built along with the logic: from lesson 01 the app opens in your browser, and every lesson that
adds a feature adds its screen, so each lesson ends in something you click through. And from lesson 02,
every lesson ends by proving itself with tests, the edge cases included.

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
  Erp.Integration.Tests/            the whole API against a throwaway PostgreSQL in a container
  Erp.Architecture.Tests/           fail the build when a module breaks a boundary
  Erp.Web.UiTests/                  Playwright, against a running app
deploy/postgres/create-roles.sql    the two production database accounts
deploy/compose/                      compose.yaml, .env.example, Caddyfile, migrate and backup scripts
```

Three rules hold it together:

1. **Modules are islands with one service window.** Everything in a module is `internal` except its entry
   class. Another module may reference only its `.Contracts` project, never the module itself.
2. **No SQL crosses a schema.** Each module owns its schema and its DbContext. Cross-module work goes
   through a contract or an event.
3. **The domain is pure.** `Domain/` depends on nothing but BuildingBlocks, so every rule is unit-tested
   without a database.

## Running the reference build

You need the .NET 10 SDK and PostgreSQL 16 or later on `localhost:5432`, as the `postgres` user with password
`erp_dev_only`. The easy way is a container (lesson 12):

```bash
podman run -d --name erp-postgres -e POSTGRES_PASSWORD=erp_dev_only -p 5432:5432 \
  -v erp-pgdata:/var/lib/postgresql docker.io/library/postgres:18
```

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

Also there: **`/scalar`** (the API, clickable; the sign-in cookie works there too), **`/openapi/v1.json`** (its
description), **`/errors`** (every error code), and **`/health/live`** and **`/health/ready`**.

To see logs, traces and metrics while you work, run the Aspire AppHost instead. It needs no containers:

```bash
dotnet run --project src/Erp.AppHost
```

And the tests. The integration tests start their own PostgreSQL with Testcontainers, so they need Podman (or
Docker) running; on Linux, enable the Podman socket and point `DOCKER_HOST` at it (lesson 12):

```bash
dotnet test     # 161 unit + 14 architecture + 114 integration; the 8 browser tests skip without ERP_UI_URL
ERP_UI_URL=http://localhost:5264 dotnet test --project tests/Erp.Web.UiTests   # with the app running
dotnet test --coverlet --coverlet-output-format cobertura                    # coverage (lesson 11)
cd tests/Erp.Modules.Sales.UnitTests && dotnet stryker                       # mutation testing, under a minute
```

CI ([.github/workflows/ci.yml](.github/workflows/ci.yml)) runs the build and every test on each push.

The reference build is there to check your work, not to replace it. Build it yourself first, by following
the lessons. To install it on a server, follow [lesson 24](plan-business-tutorial/24-deploy-podman.md), then the
first-day steps in [lesson 25](plan-business-tutorial/25-production-checklist.md).

## Status

All five modules and their screens are built and tested (ROADMAP M1–M9). Since M9 (2026-11-14) every design
choice is checked against Microsoft's guidance and practitioners' sources in
[BEST-PRACTICES-REVIEW.md](BEST-PRACTICES-REVIEW.md). The container images build with
Podman, and the database, migrate and app containers were run together in Production mode on Podman; the full
`podman compose up` (with Caddy and the backup container) hasn't been run yet, because no Compose program was
installed here. The invoice follows RR 7-2024's
list, but a business must register the system with the BIR (CAS) and have a tax adviser review the printed
invoice before issuing real ones. [Lesson 25](plan-business-tutorial/25-production-checklist.md) lists what
isn't built yet.
