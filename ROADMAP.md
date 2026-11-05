# Roadmap — from tutorial to sellable product

The course used to end in a teaching build with deliberate shortcuts. It now ends in a working ERP a
business can run, and whose source can be sold. This file is the plan and the progress log. The rules live
in [CLAUDE.md](CLAUDE.md).

**Order of work:** code first, lessons second. Each milestone lands in the reference build
([course-validation/business/](course-validation/business/)) with the build and tests green, *then* the
lessons covering it are rewritten against that working code. Writing lessons ahead of code is how the old
course drifted.

---

## Decisions (2026-11-05)

| Decision | Choice | Replaces |
|---|---|---|
| Database | **PostgreSQL** (16+; dev on 18), snake_case, schema per module | SQL Server |
| Writes & transactions | **EF Core 10 (Npgsql)**, one DbContext per module, handlers use it directly (no repositories) | Dapper + insert/update stored procs, raw ADO.NET transactions |
| Reads | **Dapper + SQL functions** (`RETURNS TABLE`; PostgreSQL procedures can't return rows), shipped by EF migrations (`migrationBuilder.Sql`) | Stored procs applied by a custom `DatabaseInitializer` |
| Schema management | **EF Core migrations** (per-module history table); production via a migrations bundle | `001_schema.sql` scripts run on boot |
| Mediator | **`martinothamar/Mediator`** (MIT, source-generated) | Hand-rolled mediator |
| Module boundaries | Everything `internal`; **one `.Contracts` project per module** is the only public surface | Contract in `Shared` (base) / `Contracts/` folder in module (Phase 15) |
| Reference builds | **One** build | `business` + `business-phase15` |
| Front end | **Blazor WebAssembly + MudBlazor**, hosted by `Erp.Api`; cookie auth for the browser | None (Swagger only) |
| Observability | **OpenTelemetry** logs/traces/metrics, health checks, OTLP export; local dashboard via .NET Aspire | None |
| Cross-module events | **Transactional outbox** + background dispatcher | Best-effort after-commit dispatch |
| Licensing | Permissive OSS only (MIT/Apache/BSD/PostgreSQL); FluentAssertions 8 → **AwesomeAssertions**; see [THIRD-PARTY-NOTICES.md](course-validation/business/THIRD-PARTY-NOTICES.md) | Mixed |
| Modules | Identity/Admin, **Sales, Inventory, Purchasing, Invoicing** | Sales + stock reservation only |
| Tutorial shortcuts | **Resolved** in the product (credit exposure, real stock movement, approval workflow, outbox, rate limiting/HTTPS) | Documented as ⚠️ limitations |

---

## Target solution layout

```
Erp.slnx
src/
  Erp.Api/                         host: Program.cs, auth, OpenTelemetry, health, hosts the Blazor app
  Erp.Web/                         Blazor WebAssembly + MudBlazor
  Shared/
    Erp.Shared.BuildingBlocks/     Entity, AggregateRoot, DomainException, contracts for ICurrentUser, IClock…
    Erp.Shared.Infrastructure/     EF/Dapper plumbing, outbox, security, telemetry (referenced only by Erp.Api)
  Modules/
    Identity/   Erp.Modules.Identity      (+ .Contracts once another module needs it)
    Sales/      Erp.Modules.Sales         + .Contracts
    Inventory/  Erp.Modules.Inventory     + .Contracts
    Purchasing/ Erp.Modules.Purchasing    + .Contracts
    Invoicing/  Erp.Modules.Invoicing     + .Contracts
tests/
  Erp.Modules.<X>.UnitTests/       domain tests, no database
  Erp.Integration.Tests/           whole API in memory against a fresh PostgreSQL erp_test database
  Erp.Architecture.Tests/          proves the boundary rules (no module→module reference, internal-only)
```

```mermaid
flowchart LR
  Api[Erp.Api] --> Sales & Inventory & Purchasing & Invoicing & Identity
  Sales[Sales] --> InvC[Inventory.Contracts] & InvoC[Invoicing.Contracts]
  Purchasing[Purchasing] --> InvC
  Invoicing[Invoicing] --> SalesC[Sales.Contracts]
  Inventory[Inventory] --> SalesC
```

A module only ever points at another module's **Contracts**; contracts point at nothing but BuildingBlocks,
so the graph can't form a cycle.

---

## Product scope

| Module | Features |
|---|---|
| **Identity / Admin** | Login (cookie + JWT), refresh tokens, change password, **user management** (create, roles, disable, reset password), audit of logins |
| **Sales** | Customers (CRUD, block/unblock, credit limit, terms), sales orders (create, edit, submit, **approval workflow**, cancel), credit exposure check, order search |
| **Inventory** | Items & categories (CRUD, pricing), warehouses, **stock receipts, adjustments (with reason), reservations, shipments** (`OnHand` really decreases), stock levels & movement history |
| **Purchasing** | Suppliers, purchase orders (create, approve, send), **goods receipt → Inventory**, partial receipts |
| **Invoicing** | Invoice from shipment, payments, customer balance & aging (feeds Sales' credit check), invoice PDF (PdfSharp/MigraDoc) |

🚫 **Out of scope:** general ledger / full accounting, payroll, HR, multi-currency, multi-company, POS.

⚠️ **BIR compliance (Philippines).** Software that issues invoices may need BIR registration (CAS/CBA) and
e-invoicing (EIS) support. The Invoicing module will be built to sound practice (sequential, gap-free
numbering, no deletes, void-with-reason), but **what BIR requires must be confirmed with a tax adviser
before selling.**

---

## Milestones

Status: ⬜ not started · 🟨 in progress · ✅ done

### M0 — Rules & plan ✅
- [x] Rewrite [CLAUDE.md](CLAUDE.md) for the new stack and production bar
- [x] This roadmap

### M1 — Foundation swap (existing features, new plumbing) ✅ code · ✅ lessons
Goal: the current Sales/Inventory features behave exactly as before, on the new stack. Tests stay green.
- [x] Spike: `martinothamar/Mediator` 3.0.2 and `internal` handlers in another assembly → generator in the
      host fails with `CS0122`; **works when the module grants `InternalsVisibleTo("Erp.Api")`**. Adopted.
- [x] Spike: `dotnet ef` works with `internal` DbContexts via an internal `IDesignTimeDbContextFactory`;
      migration history table per schema
- [x] Merge builds: `business-phase15` code copied over `business`; `business-phase15` removed
- [x] Identity extracted from `Erp.Api/Auth` into `Erp.Modules.Identity` (brought forward from M3)
- [x] `Erp.Modules.Inventory.Contracts` holds `IStockReservation`; Sales references only that
- [x] Hand-rolled mediator replaced by `martinothamar/Mediator`; logging + validation behaviors ported
- [x] PostgreSQL + EF Core: per-module DbContext, value converters for ids/`Money`/`Quantity`, owned order lines
- [x] Handlers use the DbContext directly (no repositories); explicit transaction only where a contract joins in
- [x] Shared connection (`ISharedConnection`) so Inventory joins Sales' transaction — proven by a rollback test
- [x] Reads: Dapper + SQL functions shipped by migration `AddReadFunctions`; `DatabaseInitializer` deleted
- [x] Concurrency: `xmin` row version on aggregates (+ root touch for line-only edits, with a test); atomic
      `ExecuteUpdate` for stock reservation (race test)
- [x] Refresh-token rotation race closed (`revoked_utc` concurrency token → treated as reuse)
- [x] Cross-schema FKs to `identity.app_user` removed
- [x] AwesomeAssertions; every package license checked;
      [THIRD-PARTY-NOTICES.md](course-validation/business/THIRD-PARTY-NOTICES.md)
- [x] Patched transitive versions pinned (`CentralPackageTransitivePinningEnabled`)
- [x] Architecture tests for the boundary rules (6)
- [x] Errors are RFC 9457 `application/problem+json` with a `traceId`
- [x] Unhandled domain events removed (only `SalesOrderApproved`, which has a handler); MSG0005 stays on

### M2 — Production bar on existing scope ✅ code · ✅ lessons
- [x] Transactional outbox: `OutboxInterceptor` (same SaveChanges) + `OutboxDispatcher` (background,
      `FOR UPDATE SKIP LOCKED`, retries, at-least-once); handlers no longer publish
- [x] OpenTelemetry logs/traces/metrics (OTLP when configured), `X-Trace-Id` header, `traceId` in errors,
      `/health/live` + `/health/ready`, `TelemetryBehavior` (span + duration metric per command/query)
- [x] Aspire AppHost (dev-only dashboard; no Docker) — verified it starts the API and the dashboard
- [x] Credit exposure counts orders awaiting approval; submit locks the customer row (`FOR UPDATE`)
- [x] Approval workflow: ≤ ₱50,000 auto-approved on submit; above → manager approval; creator can't approve
      own order; reject with reason releases stock; cancel reverses charge and releases stock
- [x] **Auth moved to ASP.NET Core Identity** (user request): login/refresh/cookies/lockout/password policy from
      the library; custom JWT + refresh-token code deleted; `/auth/register` blocked; data-protection keys in
      PostgreSQL; sign-out-everywhere via security stamp. Forced 90-day password expiry dropped (NIST SP 800-63B)
- [x] Rate limiting (per user/IP; strict per-IP on `/auth`), HTTPS redirection + HSTS, security headers, CORS
      from config, no `Server` header
- [x] Least privilege: `deploy/postgres/create-roles.sql` (`erp_owner` migrates, `erp_app` rows only);
      verified — full integration suite passes as `erp_app`, and `erp_app` cannot DROP/CREATE/ALTER
- [x] Search count is a separate function, so a page past the end reports the real total
- [x] Found and fixed: `Money` deserialised as ₱0 from the outbox (`[JsonConstructor]`), caught by a new test
- [x] Code comments trimmed to the non-obvious *why* (user request)

### M3 — Complete Sales, Inventory, Identity ⬜
- [ ] User management (Identity module already extracted in M1): admin creates/disables users, resets passwords
- [ ] Real `IEmailSender<AppUser>` (SMTP via MailKit, MIT) so `/auth/forgotPassword` actually sends mail —
      today it silently sends nothing
- [ ] Quantities become `numeric(18,3)` when items gain units of measure
- [ ] Customers CRUD; items/categories CRUD; warehouses
- [ ] Stock receipts, adjustments, shipments, movement history
- [ ] Sign-in audit: who signed in when, and failed attempts (in the product scope; promised in lesson 16)
- [ ] Decide with the first customer: may a SalesRep see/change every order, or only their own? Today: every
      order. If "own only", it's a handler rule via `ICurrentUser`

### M4 — Blazor front end ⬜
- [ ] `Erp.Web` (WASM + MudBlazor) hosted by `Erp.Api`; cookie auth; role-aware navigation
- [ ] Screens for M3 features: orders (with line editing), customers, items, stock, users
- [ ] CSP and security headers for the hosted app
- [ ] Two-factor sign-in (Identity's authenticator-app support) switched on, with its setup screen

### M5 — Purchasing ⬜
### M6 — Invoicing ⬜ (BIR check first)
### M7 — Packaging & install 🟨
- [x] Migration bundles per module, run as `erp_owner` (verified 2026-11-06) — lesson 15
- [x] First-run admin from `InitialAdmin` settings (brought forward: a production install had no way to sign
      in) — lesson 15, `FirstRunTests`
- [x] IIS guide — lesson 15. ⚠️ Written from Microsoft's docs; **run it once on a real Windows Server 2019+**
- [x] Backup (nightly `pg_dump`) and restore drill (verified 2026-11-06) — lesson 16
- [ ] **Encrypt data-protection keys at rest** (certificate; the app warns `No XML encryptor configured`).
      Before the first sale
- [ ] Release checklist
- [ ] Buyer monitoring guide: "quick live view" (standalone Aspire dashboard — in-memory, no alerts) vs
      "real monitoring" (free OTLP backend with retention + alerts, e.g. SigNoz or Grafana stack); just
      `OTEL_EXPORTER_OTLP_ENDPOINT` either way. The Aspire AppHost stays dev-only.
- [ ] The 2012 R2 dev server is out of support (ESU ended 2026-10-13) and can't run lesson 15 as written; see
      [runbooks/14b-deploy-ws2012r2.md](runbooks/14b-deploy-ws2012r2.md). Upgrade, or ask for a 2012 R2 variant

### L — Lessons ✅ (M1 + M2)
Rewritten against the M1 + M2 build (2026-11-06); the old phase files are deleted. Lessons for M3+ are written when that code lands.

Domain before database: with EF Core the schema follows the model, and the rules get unit tests from day one.

| # | Lesson | Replaces |
|---|---|---|
| 00 / 00a / 00b | Start here · Foundations · PowerShell | same files, updated |
| 01 | Solution setup — modules, `internal`, central packages, base types | 01 + 15 + 15b |
| 02 | Mediator — the pain, then `martinothamar/Mediator` + behaviors | 02 |
| 03 | Domain model + its unit tests | 05 |
| 04 | Database — PostgreSQL, EF Core per module, shared connection, migrations, seed | 03 + 03b |
| 05 | Sign-in — ASP.NET Core Identity | 04, 04a–04e |
| 06 | Create an order (+ error responses) | 06 |
| 07 | Submit — Inventory module, contract, one transaction, credit lock | 07 |
| 08 | Approve, reject, cancel | 08 |
| 09 | Update an order — lost updates and `xmin` | 09 |
| 10 | Reads — Dapper + SQL functions | 10 + 03a |
| 11 | Events and the outbox | 11 |
| 12 | Observability — OpenTelemetry, health, Aspire | **new** |
| 13 | Security hardening — rate limits, HTTPS, headers, least privilege | parts of 13 |
| 14 | Testing — integration and architecture tests | 12 |
| 15 | Deploy to IIS | 14b |
| 16 | Production checklist & going further | 13 + 14 |

⚠️ A reader's migration history is longer than the reference build's (e.g. `AddOutbox` in lesson 11); the
reference squashed each module into one `InitialCreate` before release. The resulting schema is identical.

**What each lesson introduces / refactors** (the "only when needed" rule — keep this in sync):

| # | Introduces | Refactors (before → after) |
|---|---|---|
| 01 | slnx, Directory.Build.props, empty Directory.Packages.props (CPM), 4 projects, `SalesModule.AddSalesModule()` | — |
| 02 | Mediator.Abstractions, FluentValidation (+DI ext), Logging.Abstractions, Carter, Mediator.SourceGenerator; `LoggingBehavior`, `ValidationBehavior`; IVT Erp.Api; Ping slice | — |
| 03 | `Entity`, `AggregateRoot` (empty), `DomainException`, `Roles`; Money/Quantity/ids, Customer, ItemCategory, SalesOrder (+status rules, ApprovalPolicy, OrderNumbering); unit test project (xunit, runner, Test.Sdk, AwesomeAssertions) | — |
| 04 | PostgreSQL install; Npgsql; Npgsql EF, NamingConventions, EF Design; transitive pinning + EF Relational pin; `IModuleDatabase` (Module, MigrateAsync(ct), Seed(ct)), `EmbeddedSql`; `NpgsqlDataSource`; `PostgresDatabase.EnsureExistsAsync`; `Item` + private EF ctors; SalesDbContext `Configure(o, NpgsqlDataSource)` (no xmin, no sequence); dotnet-ef manifest; DevSeed; DatabaseStartup | — |
| 05 | Identity (AddIdentityApiEndpoints, MapIdentityApi, /register blocked, roles seeded via HasData, dev users via UserManager), `ICurrentUser`+`CurrentUser`, `/auth/me`, sign-out-everywhere | — |
| 06 | `IClock`/`SystemClock`, `NotFoundException`, `GlobalExceptionHandler` (problem+json: 400/404/422/500), Swagger (Swashbuckle), order-number sequence (`HasSequence` + `NextOrderNumberAsync`), create slice, `PendingTotalAsync`; delete Ping | — |
| 07 | Inventory module + Contracts project, `IStockReservation`, atomic `ExecuteUpdate`, `LockCustomerAsync` (FOR UPDATE) | `NpgsqlDataSource` per context → `ISharedConnection` + `Configure(o, DbConnection)` (one transaction across modules) |
| 08 | Approve/reject/cancel slices; `xmin` row version on Customer + SalesOrder (two approvals race on a customer balance) | GlobalExceptionHandler gains `DbUpdateConcurrencyException` → 409 |
| 09 | Update slice | `SaveChangesAsync` override: touch root on line-only edits |
| 10 | Dapper, `IDbConnectionFactory`, SQL functions + `AddReadFunctions` migration, `MatchNamesWithUnderscores`, get + search slices | — |
| 11 | `IDomainEvent`, events on `AggregateRoot`, `IAggregateRoot`, `OutboxMessage`, `OutboxInterceptor`, `OutboxDispatcher`, `IModuleDatabase.Schema`, `SalesOrderApproved` + audit handler, `[JsonConstructor]` (round-trip test shows the bug) | — |
| 12 | OpenTelemetry packages, `ErpTelemetry` + `Observability.cs` (Infrastructure/host), `X-Trace-Id`, health checks, `DatabaseHealthCheck`, Aspire AppHost | `LoggingBehavior` (BuildingBlocks) → `TelemetryBehavior` (Infrastructure) |
| 13 | `RateLimitPolicies`, rate limiting, HSTS/HTTPS, `SecurityHeadersMiddleware`, CORS from config, `ApiKeyMiddleware`, no Server header, create-roles.sql, DataProtection.EntityFrameworkCore (keys in DB) | `IModuleDatabase.MigrateAsync(ct)` → `MigrateAsync(ownerConnection, ct)` + `ErpDbMigrations` |
| 14 | `public partial class Program`, integration tests (Mvc.Testing, Npgsql, Dapper), architecture tests | — |
| 15 | `InitialAdmin` (`IHostedService`) + `InitialAdmin` settings, `FirstRunTests` + `OwnDatabaseFact`; migration bundles; IIS + Hosting Bundle, app pool (No Managed Code, AlwaysRunning, no idle timeout/recycle) with settings as app pool environment variables, preload, HTTPS binding, `removeServerHeader`/`X-Powered-By`, `app_offline.htm` redeploy | `CustomWebAppFactory.DropTestDatabase()` → `DropDatabase(connectionString)` |
| 16 | Nightly `pg_dump` (scheduled task, password file), restore drill; done / missing checklist; when to add left-out tools | — |

---

## Risks to retire early

- ~~**Source-generated mediator + `internal` handlers in other assemblies.**~~ Retired 2026-11-05:
  `InternalsVisibleTo("Erp.Api")` per module; an architecture test stops it spreading to other modules.
- ~~**EF tooling with internal DbContexts**~~ Retired 2026-11-05: internal design-time factories work.
- ~~**Sharing one transaction across module DbContexts**~~ Retired 2026-11-05: `ISharedConnection` +
  `UseTransaction`; `A_reservation_rolls_back_with_the_callers_transaction` proves it.
- **Course length.** Adding four modules and a UI roughly doubles the course. Lessons for the new modules
  should lean on patterns already taught and only teach what's new.
- **Selling source vs. a public course.** If the course is published openly and ends in the full product,
  the product's source is effectively free. Decide how the course is distributed before M4.

---

## Log

- **2026-11-05** — Direction changed (see Decisions). CLAUDE.md rewritten; roadmap created. Baseline
  `business-phase15`: 14 unit + 8 integration tests pass.
- **2026-11-05** — Database switched to PostgreSQL (user decision). PostgreSQL 18 installed locally via winget
  (dev superuser password `erp_dev_only`, dev only). M1 code complete on PostgreSQL: **14 unit + 6 architecture
  + 10 integration tests pass.** Lessons not yet converted.
- **2026-11-05** — M2 code complete: **22 unit + 6 architecture + 23 integration tests pass** (also as the
  least-privilege `erp_app` role). Auth switched to ASP.NET Core Identity at the user's request.
- **2026-11-06** — Lessons 00–16 rewritten for M1 + M2 in the WRITING-STYLE voice; old phase files deleted;
  glossary, answer key, foundations, PowerShell companion, resources and README rewritten. Added the
  first-run admin; 400 responses now carry a readable `detail`, and problem details declare UTF-8. Verified
  live: migration bundles, the published app in Production as `erp_app`, backup → restore onto fresh roles,
  and the PowerShell companion on PS 5.1. **22 unit + 6 architecture + 25 integration tests pass.** IIS itself
  not yet run (see [course-validation/FINDINGS.md](course-validation/FINDINGS.md)).
