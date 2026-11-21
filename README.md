# YASDA — Yet Another Service Desk Application

An open-source service desk you build, then run: requesters file tickets, agents work queues, SLAs keep
everyone honest, and a knowledge base answers the common questions before they're asked. It's built as a
**modular monolith** with **vertical slices** on .NET 10 and PostgreSQL, and comes with a course that teaches
you to build it. MIT-licensed ([LICENSE](LICENSE)); every dependency is free and permissively licensed.

**Status, 2026-11-19: rebuilding.** Until that day this repo built a Philippine ERP; it was replaced by YASDA
to leave every domain that needs BIR registration or permits (the ERP is the git tag `erp-final`). The
platform carried over: sign-in, staff accounts, the outbox, observability, security, Podman deployment and
every kind of test. The helpdesk modules are being built now, milestone by milestone
([ROADMAP.md](ROADMAP.md)). **The course grows with it**: each milestone ships its lessons, starting from
[00 — Start here](plan-business-tutorial/00-START-HERE.md), and assumes you have nothing installed yet.

| | |
|---|---|
| Architecture | Modular monolith; each module is `internal` behind one public Contracts project; vertical slices inside |
| Modules | Identity, Tickets, Sla and Knowledge today ([ROADMAP.md](ROADMAP.md)) |
| Stack | .NET 10 minimal APIs (`TypedResults`, no mediator: endpoints call their handler), FluentValidation, EF Core (writes), Dapper + SQL functions (reads), PostgreSQL |
| Screens | Blazor WebAssembly with **no component library**: plain HTML in the look of [DESIGN.md](DESIGN.md) (warm paper, one serif, blue links), served by the API itself under the same address; the API lives under `/api` |
| Errors | Every error carries a stable code (`IDENTITY-0003`) and a trace id; the app's **Error codes** page explains each code and its fix |
| Sign-in | ASP.NET Core Identity: cookies for the browser, bearer and refresh tokens for programs, lockout, five roles (Admin, TeamLead, Agent, KbEditor, Requester), admin-managed accounts |
| Production | Transactional outbox, OpenTelemetry, health checks, rate limits, HTTPS/HSTS, a strict Content-Security-Policy, certificate-encrypted sign-in keys, least-privilege database roles |
| Deployment | Compose on rootless Podman (Docker runs the same files), Caddy for HTTPS, nightly backups, on any Linux server |
| Tests | xUnit v3 + AwesomeAssertions unit tests, FsCheck property tests, integration tests on a throwaway PostgreSQL (Testcontainers) run as the least-privilege role, architecture tests, Playwright browser tests; coverlet coverage and Stryker.NET mutation testing; GitHub Actions CI |
| Licenses | MIT for YASDA; permissive open source only for everything it uses ([THIRD-PARTY-NOTICES.md](course-validation/business/THIRD-PARTY-NOTICES.md)) |

For contributors: [CLAUDE.md](CLAUDE.md) holds the rules, [DESIGN.md](DESIGN.md) the look,
[WRITING-STYLE.md](WRITING-STYLE.md) the lesson voice, [ROADMAP.md](ROADMAP.md) the plan and progress
([ROADMAP-HISTORY.md](ROADMAP-HISTORY.md) the finished milestones, the log and the ERP's history).

## Solution layout

```
Yasda.slnx
Directory.Build.props, Directory.Packages.props   shared settings; every package version, once
Dockerfile, .dockerignore                         the app and migrate images
src/
  Yasda.Api/                          the host: Program.cs, error handling, security, observability; serves Yasda.Web
  Yasda.Web/                          the Blazor WebAssembly app: pages, layout, ApiClient, app.css
  Yasda.AppHost/                      development only: runs the API next to the Aspire dashboard
  Shared/
    Yasda.Shared.BuildingBlocks/      what every module may use: base classes, roles, error codes, small interfaces
    Yasda.Shared.Infrastructure/      implementations: connections, outbox, telemetry. Only the host sees it
  Modules/
    Identity/Yasda.Modules.Identity/  sign-in (ASP.NET Core Identity), accounts, teams, sign-in log, email
    Identity/Yasda.Modules.Identity.Contracts/  the only part other modules may reference (IUserDirectory, ITeamDirectory)
    Tickets/Yasda.Modules.Tickets/    tickets, their conversation, queues, categories and routing
    Tickets/Yasda.Modules.Tickets.Contracts/  what Tickets tells other modules: its events and priorities
    Sla/Yasda.Modules.Sla/            SLA clocks and policies: how long a ticket has to be answered and resolved
    Sla/Yasda.Modules.Sla.Contracts/  what Sla tells other modules: a ticket's deadlines
    Knowledge/Yasda.Modules.Knowledge/  help articles: drafts, review, published versions, an audit trail, what readers see
      Domain/                         the rules, no database
      Features/                       one folder per feature: XxxHandler.cs + XxxEndpoint.cs
      Infrastructure/                 DbContext, migrations, SQL functions
      IdentityModule.cs               the module's one public type
tests/
  Yasda.Modules.<Module>.UnitTests/   each module's rules, no database
  Yasda.Integration.Tests/            the whole API against a throwaway PostgreSQL in a container
  Yasda.Architecture.Tests/           fail the build when a module breaks a boundary
  Yasda.Web.UiTests/                  Playwright, against a running app
  Yasda.Web.UnitTests/                the browser app's own helpers (times as words)
deploy/postgres/create-roles.sql      the two production database accounts
deploy/compose/                       compose.yaml, .env.example, Caddyfile, migrate and backup scripts
```

Three rules hold it together:

1. **Modules are islands with one service window.** Everything in a module is `internal` except its entry
   class. Another module may reference only its `.Contracts` project, never the module itself.
2. **No SQL crosses a schema.** Each module owns its schema and its DbContext. Cross-module work goes
   through a contract or an event.
3. **The domain is pure.** `Domain/` depends on nothing but BuildingBlocks, so every rule is unit-tested
   without a database.

## Running the reference build

You need the .NET 10 SDK and PostgreSQL 16 or later on `localhost:5432`, as the `yasda_dev` user (a superuser, so the app can create its database) with password
`yasda_dev_only`. The easy way is a container:

```bash
podman run -d --name yasda-postgres -e POSTGRES_USER=yasda_dev -e POSTGRES_PASSWORD=yasda_dev_only -p 5432:5432 \
  -v yasda-pgdata:/var/lib/postgresql docker.io/library/postgres:18
```

```bash
cd course-validation/business
dotnet run --project src/Yasda.Api
```

In Development, the app creates the `yasda` database, applies every module's migrations and seeds sample
data on start. Open **http://localhost:5264** and sign in. The seeded users all have the password
`Password123!`:

| Email | Role |
|---|---|
| `admin@yasda.local` | Admin |
| `lead@yasda.local` | TeamLead |
| `agent@yasda.local` | Agent |
| `editor@yasda.local` | KbEditor |
| `requester@yasda.local` | Requester |

Also there: **`/scalar`** (the API, clickable; the sign-in cookie works there too), **`/openapi/v1.json`** (its
description), **`/errors`** (every error code), and **`/health/live`** and **`/health/ready`**.

To see logs, traces and metrics while you work, run the Aspire AppHost instead. It needs no containers:

```bash
dotnet run --project src/Yasda.AppHost
```

And the tests. The integration tests start their own PostgreSQL with Testcontainers, so they need Podman (or
Docker) running; on Linux, enable the Podman socket and point `DOCKER_HOST` at it:

```bash
dotnet test     # 276 unit + 14 architecture + 289 integration; the 45 browser tests skip without YASDA_UI_URL
# Browser tests, with the app running. Every test signs in, so raise the sign-in limit for the run:
#   RateLimiting__AuthRequestsPerMinute=1000 dotnet run --project src/Yasda.Api
YASDA_UI_URL=http://localhost:5264 dotnet test --project tests/Yasda.Web.UiTests
dotnet test --coverlet --coverlet-output-format cobertura                      # coverage
cd tests/Yasda.Modules.Identity.UnitTests && dotnet stryker                    # mutation testing
```

CI ([.github/workflows/ci.yml](.github/workflows/ci.yml)) runs the build and every test on each push.

To install it on a server: copy [deploy/compose/](course-validation/business/deploy/compose/) to a Linux server
with Podman, fill in `.env` from [.env.example](course-validation/business/deploy/compose/.env.example), and run
`podman compose up -d`. The step-by-step install guide arrives with lesson 28.
