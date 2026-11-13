# CLAUDE.md — Guardrails for This Repository

> **Read this before editing anything.** This repo is a **course whose end result is a real, sellable
> product**: a working ERP (Sales, Inventory, Purchasing, Invoicing) built as a **Modular Monolith with
> Vertical Slice Architecture (VSA)**, with a Blazor front end, that a business can actually run. The
> lessons teach the reader to build it; the reference build *is* the product. The failure modes here are
> (a) **derailing** — swapping a decided technology, adding a banned one, or introducing an API the reader
> hasn't been shown — and (b) **shipping something that only looks finished**. When a request conflicts
> with a rule here, **stop and flag the conflict** instead of silently breaking the course.
>
> **Direction changed on 2026-11-05.** The course used to be a tutorial with deliberate shortcuts, a
> hand-rolled mediator, and Dapper + stored procedures for everything, with EF Core banned. It is now a
> production course. The current plan and progress live in [ROADMAP.md](ROADMAP.md). Where an older lesson
> or code file still reflects the old rules, the **rules in this file win** — that file is due for
> conversion, not a precedent.

---

## 1. What this repository is

| Path | What it is |
|---|---|
| `plan-business-tutorial/` | **The course.** The only course. Phases run in order; each ends in something runnable. Plus `00a-FOUNDATIONS.md`, `GLOSSARY.md`, `LEARNING-RESOURCES.md`, `ANSWER-KEY.md`. |
| `course-validation/business/` | **The reference build = the product.** The complete, runnable app the course ends in. Every lesson's code must match it. `FINDINGS.md` is the validation log. |
| `ROADMAP.md` | The conversion plan: milestones, decisions, status. Update it as work lands. |
| `README.md` | The front door. Keep it in sync with any structural change. |

**There is one reference build, and its job is to prove the tutorial works.** `course-validation/business-phase15/`
used to back the optional Phases 15/15b; those designs (per-module contracts + `internal`) are now the
**main path**, so it was folded into `course-validation/business/` and **removed on 2026-11-05**. Don't
recreate a second build.

**The course is self-contained.** No companion courses, no sibling folders. `plan-beginner-friendly/` and
`plan-Opus/` were **deleted on purpose** — do not recreate them, link to them, or reintroduce their
vocabulary (`INotification`, `Submit(bool approvalGranted)`, `tx.Inner`, `IPricingService`).

---

## 2. How this course teaches — progressive, complete, honest, *lean*

A lesson that is technically correct but skips the "why" has failed. **So has a lesson that buries the
point under explanation.** The reader should finish each phase with working code and a clear reason for
every piece of it — and no more words than that took.

1. **Assume nothing, explain once.** Don't assume DDD, CQRS, DI, `async`/`await`, EF Core, or what a race
   condition looks like. Teach a concept **once, where it's first needed**, in the fewest words that make
   it land. Later phases link back; they don't re-explain.
2. **Progressive.** Each phase builds only on what earlier phases established. Never forward-reference an
   undefined API, never say "as you know." **Each phase ends in a runnable, verifiable result.**
   **Introduce each package, tool, file, concept and config option only in the lesson that first uses it**,
   explain it there, never add anything "for later", and when a later lesson replaces something, refactor
   it with a before/after and remove the old one. Full rule: [WRITING-STYLE.md](WRITING-STYLE.md)
   ("Introduce each thing only when it's needed" and "Build up, then refactor").
3. **No phantom APIs.** Every type and method shown is defined where the reader has already met it, or
   right there, with its DI registration. Nothing on the critical path is "left as an exercise."
4. **Pain first, solution second** — but briefly. Show the naive version and why it hurts in a few
   lines, then the fix. Don't stage a long drama for a small point.
5. **Explain the reasoning, not just the mechanics** — one or two sentences of *why* per decision.
6. **Be honest about costs.** Name each pattern's downsides (EF change-tracking overhead, two data-access
   tools, a hand-written registration per handler, best-effort vs. outbox), in the voice: *"Now, let's be honest about the
   cost here: …"*
7. **Every lesson proves itself with tests (user decision, 2026-11-12).** From lesson 02 (Testing) on, each
   lesson ends with tests for what it built: the ones that teach something new in full, then a table of every
   edge case (test name + why). Each test kind is set up and explained in the lesson that first needs it. A
   test that has never failed proves nothing: make each one fail once. Plan: [ROADMAP.md](ROADMAP.md) M8.
8. **Production means production.** The final build has no "known shortcut" left in it. If an early phase
   uses a simpler version for teaching, a later phase **replaces it** and says so — the course never ends
   on a shortcut.

### The voice — read [WRITING-STYLE.md](WRITING-STYLE.md) before writing any lesson

Every lesson uses the instructor voice adapted from the *Teach me Driving* study guides (user's choice,
2026-11-05), **in English only**: a greeting and "Are you ready? Let's begin!", "you" and "let's",
check-ins like "Easy, right?", "To remember it easily:" memory aids, a warm close ("Good luck,
developer!"), and "Let's check!" recall questions with **no answers shown** (answers live in
ANSWER-KEY.md). **No `>` callouts, no emoji markers, no terse labels** ("Note:", "Heads-up:"); caveats and
WebForms translations are sentences in the voice. [WRITING-STYLE.md](WRITING-STYLE.md) has the full rules,
the section order, and a sample.

- **Don't** open with "In today's fast-paced world," "Let's dive in," "It's important to note that," "By
  leveraging," or "In conclusion," and don't end a section by summarising it.
- **Do** use concrete numbers and real names from this domain (`SalesOrder`, `SalesManager`, ₱).
- **Cut ruthlessly.** If a paragraph can lose half its words and keep its meaning, it should.
- **Consistent analogies** — the outbox dispatcher = *post office* (it delivers each event to whoever
  subscribed; the sender never knows who); atomicity = *turnstile*; modules = *islands with one
  service window* (the contracts project); VSA = *taco drawer*; value object = *any ₱1,000 bill is as good
  as any other*. Don't invent competing ones.
- **Diagrams:** Mermaid blocks. An image (in `plan-business-tutorial/images/`) only when Mermaid can't
  express it, e.g. a UI screenshot.
- **File references in prose use Markdown links**, not backticks or bare paths.

---

## 3. The locked stack

.NET 10 · C# · **Modular Monolith + VSA** · **minimal APIs** (`MapGroup`, `TypedResults`; each module maps its own
endpoints) · **PostgreSQL** (16+; developed on
18), schema-per-module, snake_case names (`EFCore.NamingConventions`) · **EF Core (Npgsql) for every write and
every transaction** · **Dapper + schema-qualified SQL functions for reads (SELECTs only)** — PostgreSQL
procedures can't return result sets, so the read side is `RETURNS TABLE` functions — **shipped through EF Core
migrations** · **no mediator library**: endpoints call their handler directly (decided 2026-11-13, see
[BEST-PRACTICES-REVIEW.md](BEST-PRACTICES-REVIEW.md)) · **CQRS-lite** · **FluentValidation**, run by one endpoint
filter on `/api` · **stable error codes** on every problem response, with a catalog page ·
**ASP.NET Core Identity** (`AddIdentityApiEndpoints` / `MapIdentityApi`): cookies for the browser, bearer +
refresh tokens for other clients, lockout, password policy, role-based authorization; keys persisted to
PostgreSQL · **Blazor Web App + MudBlazor**, global interactive WebAssembly (prerender off), served by `Erp.Api`, built along the way (every feature lesson
adds its screen) · the API lives under **`/api`** · **OpenTelemetry** (traces, metrics, logs, OTLP export) +
health checks; **Aspire AppHost** as the dev-only dashboard · **`Microsoft.AspNetCore.OpenApi`** + **Scalar**
(dev-only API page) · `TimeProvider` (tests: `FakeTimeProvider`) · **transactional outbox** · **xUnit v3 on
Microsoft.Testing.Platform + AwesomeAssertions**, **FsCheck.Xunit.v3** (property-based), **Testcontainers.PostgreSql**
(from lesson 12), **coverlet.MTP**, **Stryker.NET** (`test-runner: mtp`), **GitHub Actions** CI · analyzers at
`latest-recommended`, warnings fail the CI build · **PdfSharp/MigraDoc 6.2.4** for invoices · **MailKit** for
email · containers: **Podman** (user decision 2026-11-12) for the dev database (from lesson 12), tests and
deployment (Linux containers, **Compose** files run with `podman compose`, Caddy for HTTPS). Keep every container
file **Docker-compatible** (plain Compose spec, OCI `Dockerfile`, no Quadlet or other Podman-only format) and say
once, where it matters, that Docker runs the same files. Lessons 05–11 use a natively installed PostgreSQL on
purpose: lesson 12 shows the pain, then moves to containers. IIS was dropped on 2026-11-06 (user decision); don't
reintroduce an IIS path, `web.config` or Windows-hosting lesson.

> **Direction update (2026-11-06, user decision).** The product runs in containers (Podman since 2026-11-12); the
> interface is built in the course alongside the logic: lesson 01 creates the Blazor app, lesson 06 signs in through it, and every
> feature lesson adds its screen. There is no separate test console and no separate "interface" lesson. The browser app signs in with Identity's
> cookie; there are **no API keys** (a key shipped in a browser app is public). Add machine-client keys only
> when a non-person client exists.

> **Auth is library-only (2026-11-05).** No hand-written JWT minting, refresh-token tables, password hashing
> or token rotation. If Identity can't do something, say so and choose another *library* (OpenIddict for
> SSO/OAuth) rather than writing token code. `/auth/register` is blocked: staff accounts are created by admins.

> **Database changed on 2026-11-05: SQL Server → PostgreSQL.** Any remaining `usp_`, `rowversion`, `GO`,
> `[brackets]`, LocalDB or `Microsoft.Data.SqlClient` in a lesson is stale and due for conversion.

### Licensing rule — the source code is sold

Buyers receive the source and inherit every dependency's licence. **Only permissive open-source licences
(MIT, Apache-2.0, BSD).** Never a per-developer commercial licence, a revenue-threshold "community"
licence, or a copyleft licence that would bind the buyer. Known traps, all **banned**: MediatR (paid since
2025), FluentAssertions ≥ 8 (paid), AutoMapper (paid since 2025), MassTransit ≥ 9 (paid), QuestPDF
(revenue-gated — use PDFsharp/MigraDoc **6.2.4**: 6.5 and later are AGPL-3.0, checked 2026-11-06), Telerik/Syncfusion/DevExpress, Seq (commercial), Duende
IdentityServer (revenue-gated). **Check the
licence of any package before adding it**, and record it in the third-party notices file.

### Banned in the built solution
Clean Architecture's four-layer project split · a generic `Repository<T>` · an `IUnitOfWork` wrapper over
`DbContext` (the DbContext *is* the unit of work; don't hide it) · EF Core for **reads** in query handlers
(reads are Dapper) · Dapper for **writes** · message brokers (RabbitMQ, Kafka, MassTransit) · Redis /
distributed caching · microservices · AutoMapper · Kubernetes or any orchestrator as a requirement (Compose on
one host is the target) · Docker Desktop as a requirement for *servers* (it's paid for larger companies; servers
run the free Docker Engine, and the course's path is Podman) · hand-written authentication/token code (see above).

These may appear only in a lesson's *"when you'd add this later, and why not now"* discussion.

---

## 4. The non-negotiable architecture rules

1. **Modules are islands with one service window.** A module is `Erp.Modules.X` (**everything `internal`**
   except its one public entry class, e.g. `SalesModule.AddSalesModule`) plus, as soon as another module
   needs something from it, `Erp.Modules.X.Contracts` (**the only public surface**: contract interfaces,
   their DTOs, integration events). Don't create an empty Contracts project ahead of need. A module may reference **other modules'
   `.Contracts` projects only — never another module's main project.** Contracts projects reference nothing
   but `Erp.Shared.BuildingBlocks`. This is what keeps the reference graph acyclic when modules call each
   other in both directions.
2. **No cross-schema SQL.** No `JOIN` or FK across module schemas (not even to `identity.users` for a
   "created by" column — store the id, no FK); no DbContext mapping another module's tables. Cross-module data goes through a contract call or an integration event.
3. **`internal` has framework consequences — keep the fixes.** A module's public surface is exactly two
   methods: `AddXModule()` (registers its DbContext, handlers, validators, event handlers and error catalog) and
   `MapXEndpoints()` (maps its endpoints). Handlers are registered **by hand**, one line each, and a test proves
   every one is registered; `AddValidatorsFromAssembly` **must** pass `includeInternalTypes: true`. .NET 10's
   built-in `AddValidation()` silently skips internal models (spike, 2026-11-13), which is why validation is
   FluentValidation. `InternalsVisibleTo` is granted **only to test assemblies** — not even to `Erp.Api`, so the
   compiler keeps the host away from a module's internals (the old Mediator generator needed that grant; it's
   gone). Keep the regression tests that prove each of these (e.g.
   `Order_with_no_lines_is_rejected_by_the_internal_validator`, `Every_feature_handler_is_registered`) and the
   architecture test that fails if a module grants `InternalsVisibleTo` to anything but a test project.
4. **Keep the Domain pure.** `Domain/` depends only on `Erp.Shared.BuildingBlocks`. No EF Core attributes,
   SQL, ASP.NET Core, Dapper, or `HttpContext` in the domain. EF mapping lives in `Infrastructure/` as
   `IEntityTypeConfiguration<T>` (fluent API only).
5. **`Shared/` is exactly two projects.** `Erp.Shared.BuildingBlocks` (one flat namespace; folders
   `Domain/`, `Abstractions/`, `Messaging/`) depends on nothing heavy and holds what every module may use.
   `Erp.Shared.Infrastructure` holds implementations and is referenced **only by `Erp.Api`**. Don't re-split
   `BuildingBlocks` — *a project boundary exists to make something a compile error; folders organise code
   you're allowed to use.*
6. **Where each kind of logic lives** — the core skill the course teaches. Keep it sacred:

   | Concern | Home |
   |---|---|
   | "Is the request well-formed?" (shape: positive ids, ≥1 line) | **Validator** (FluentValidation) on the type the endpoint binds: a request in the endpoint file, or a query record in the handler file. Run by the `/api` filter; answered 400 with `ERP-0001`, no exception |
   | "Is this operation allowed?" (blocked customer, credit limit, status transition, approval threshold) | **Domain** — a method on the entity/value object, throwing `DomainException(XErrors.Code, detail)` → 422 with the code |
   | "Does the thing in the URL exist?" | **Handler** returns `null`/`false`; the **endpoint** answers `TypedResults.NotFound()`. An unknown id *inside the body* is a coded 422 |
   | "Load → do → save" coordination | **Command handler** (EF Core, no business `if`s) |
   | Reading data for a screen or report | **Query handler** → Dapper → SQL function |
   | Safety when two people act at once (reserve stock, lost-update guard) | **EF Core** — the `xmin` row version as a concurrency token, or one atomic `ExecuteUpdate` with a guard in the `WHERE` |
   | One module needs another (must be same transaction) | **Synchronous contract** in the other module's `.Contracts` |
   | Something reacts afterward, in the same module | **Domain event** (internal) raised by the aggregate → `OutboxInterceptor` writes it in the same `SaveChanges` → `OutboxDispatcher` delivers it to the module's `IDomainEventHandler<T>` after commit. At-least-once: **handlers must be idempotent** |
   | Another module reacts afterward | The owning module's domain event handler maps it to a public **integration event** (`.Contracts`) and calls `IIntegrationEventPublisher`, which hands it to every `IIntegrationEventHandler<T>` inside the same outbox delivery |
   | "This endpoint is role-X only, full stop" | **Endpoint** (`RequireAuthorization` / role policy) |
   | Auth that depends on the data ("your own customers only") | **Handler/domain** via `ICurrentUser` |

   A business `if` in a handler, a business rule in a validator or stored procedure, or a rule that needs a
   database to unit-test is a **defect**.

---

## 5. Conventions to preserve

- **Two files per feature:** `XxxHandler.cs` (Command/Query + Result/DTOs + Handler, plus the validator of a query
  bound with `[AsParameters]`) and `XxxEndpoint.cs` (Request + its validator + a static `Map` method). Closely related actions on one thing may share a
  feature folder (`UpdateCustomer` also holds block/unblock; `ItemCategories` holds list and save). **Never**
  technical-layer folders (`Commands/`, `Handlers/`, `Validators/`, `Endpoints/`).
- **Handlers:** plain `internal sealed class XxxHandler` with `Task<T> HandleAsync(XxxCommand|XxxQuery, CancellationToken)`,
  registered `AddScoped` in the module and injected into the endpoint. Commands and queries stay separate records
  (CQRS-lite): a query handler never takes a DbContext, a command handler never uses Dapper (architecture test).
  A command on a resource named in the URL returns `bool`/`T?` (false/null = not found). Endpoints return
  `TypedResults` with declared `Results<…>` unions, so OpenAPI lists every answer. Handlers never publish events
  themselves — the outbox does. Tracing per request comes from ASP.NET Core's OpenTelemetry instrumentation.
- **Error codes:** every `DomainException` carries an `ErrorCode` from its module's internal catalog
  (`Domain/SalesErrors.cs`: `SALES-0001`…, Title, Fix), registered with `ErrorCatalog.From`. Codes are never
  renumbered or reused; tests prove they are unique, documented and used. Every problem response has `code`,
  `type: /errors/{code}` and `traceId`; `/api/errors` serves the catalog and the app's `/errors/{code}` page shows it.
- **Time:** inject `TimeProvider` (the host registers `CompanyTimeProvider`, whose local zone is the company's);
  `time.GetUtcNow().UtcDateTime` and `time.Today()`. Tests use `FakeTimeProvider`.
- **Value objects that appear in domain events** need `[JsonConstructor]` on their validating constructor, or
  the outbox round-trip silently zeroes them (caught by `An_event_survives_the_round_trip_through_the_outbox`).
- **Base types** in `Erp.Shared.BuildingBlocks`: `Entity<TId>`, `AggregateRoot<TId>` (`where TId : struct`,
  repeated on derived classes), `IDomainEvent`, `DomainException`.
- **Strongly-typed IDs** (`readonly record struct SalesOrderId(long Value)`) with EF value converters; never a
  bare `long` id in the domain.
- **Rich domain:** private setters, `Create()` factory + private parameterless constructor for EF, computed
  `Total`, guarded state transitions, value objects (`Money`, `Quantity`) that reject invalid values.
- **Writes:** the command handler uses **the module's DbContext directly** — load the aggregate, call domain
  methods, `SaveChangesAsync`. The DbContext already *is* a repository and a unit of work; no per-aggregate
  repository, no generic `Repository<T>`, no `IUnitOfWork` wrapper. One `SaveChangesAsync` is atomic on its
  own; when a command also calls **another module's contract**, the transaction is **explicit and visible in
  the handler** (`BeginTransactionAsync` … `CommitAsync`) and is passed to the contract, which joins it
  (`UseTransaction`). A throw means rollback.
- **One DbContext per module**, mapped to that module's schema, with its own migrations history table in
  that schema. Every module DbContext in a request uses the **same `DbConnection`** (`ISharedConnection`),
  which is what lets a contract join the caller's transaction. Concurrency: a shadow
  `Property<uint>("RowVersion").IsRowVersion().HasColumnName("xmin")` on each aggregate root (PostgreSQL's
  built-in row version); `DbUpdateConcurrencyException` → 409. An aggregate whose child rows can change on
  their own (order lines) must touch the root row on save, or its version is never checked — see
  `SalesDbContext.SaveChangesAsync` and its integration test.
- **Each module's DbContext has a static `Configure(options, connection)`** used both at runtime and by its
  `IDesignTimeDbContextFactory`, so `dotnet ef` and the running app can never disagree. Table names are
  explicit and singular (`ToTable("sales_order")`).
- **Reads:** query handlers call SQL functions with Dapper (`SELECT * FROM sales.search_sales_orders(@...)`).
  Functions are **schema-qualified in their definition and every C# call**, live as one `.sql` file per read in
  the module's `Infrastructure/Sql/Functions/` named `schema.function.vN.sql` (with its companions: the
  `count_` for a search, the `_lines` for a get), and are applied by a migration
  via `migrationBuilder.Sql`. **A shipped migration or script is never edited** — a changed function gets a
  `.v2` file and a new migration. Dapper maps snake_case columns to PascalCase records
  (`MatchNamesWithUnderscores`, set once in `AddSharedInfrastructure`).
- **Domain events:** raise an event only in the same change that adds its first handler. Domain events are
  `internal`; integration events live in a module's `.Contracts` project and are plain data. Each module registers
  its own event handlers; tests prove every event has a handler and every handler is registered.
- **Dev seed data** lives in each module's `Infrastructure/Sql/DevSeed.sql`, runs only when
  `Database:SeedDevelopmentData` is true (Development/tests), and is idempotent.
- **Roles** live once in `Erp.Shared.BuildingBlocks.Roles` (`SalesRep`, `SalesManager`, `Admin`; later
  `InventoryClerk`, `Purchaser`, `Accountant`) and are seeded into `identity.roles` by migration. Staff have
  exactly one role. Never type a role string.
- **Least privilege in production:** the app connects as `erp_app` (rows only), migrations run as `erp_owner`
  via `ConnectionStrings:ErpDbMigrations` (see `deploy/postgres/create-roles.sql`).
- **Money is Philippine Pesos (₱)**, `numeric(18,2)`. Quantities are whole numbers today; they become
  `numeric(18,3)` when items gain units of measure (ROADMAP M3).
- **Business values live in tables, edited on screens; `appsettings` describes the installation** (user
  decision, 2026-11-07). The company's registered details, tax regime, VAT rate, invoice title and BIR permit
  are Invoicing's `company_profile` (Accountant/Admin, **Company and tax** page, validated by the domain); the
  special-discount programs are Sales' `special_discount_program` table (seeded SC/PWD). Only connection
  strings, certificates, SMTP, fonts, the time zone, telemetry and the first admin stay in configuration. Never
  put a rate, a TIN or a permit number in `appsettings.json` or `.env`.
- **Tax is data, never a constant.** Each item has a `TaxType` (VATable, zero-rated, VAT-exempt) and a
  special-discount flag, copied onto order and invoice lines; each invoice **stores** its regime, rate,
  breakdown, discount and a copy of the seller's details, and is never recalculated. Sales gets the tax policy
  through `IInvoicingSetup` (Invoicing.Contracts). Special discounts are computed once, by
  `SpecialDiscount.Apply` in BuildingBlocks, on the order **and** at issue, so the invoice total always equals
  the order total charged to the customer's balance. The printed invoice follows BIR RR 7-2024 Sec. 6(B),
  including (B.18); a change to its layout must keep every field on that list.
- **The host references `Erp.Web` normally, with `Aliases="ErpWeb" PrivateAssets="all"`.** Never
  `ReferenceOutputAssembly="false"`: the host would stop serving the app's package files (MudBlazor's CSS and
  JS), and the app would run unstyled. `App.razor` (the page) and `Routes.razor` both live in `Erp.Web`; the host
  maps `MapRazorComponents<ErpWeb::Erp.Web.App>().AddInteractiveWebAssemblyRenderMode().AllowAnonymous()` and sets
  `RequiresAspNetWebAssets` (it has no `.razor` files of its own, so the SDK wouldn't serve `blazor.web.js`).
- **Every dependency is permissively licensed and free** (see §3) and listed in the third-party notices file.
- **Code comments are minimal.** None on self-explanatory code. Where one is needed, one short line with the
  important, non-obvious *why* (a trap, a constraint). The lessons carry the explanations, not the code.

---

## 6. Production bar — what "done" means

The finished product has **no deliberate shortcuts**. In particular the old tutorial simplifications are
**resolved**, not documented:

- Credit limit counts committed **and** in-flight (submitted/approved, unbilled) exposure.
- Stock moves for real: receiving increases `OnHand`, shipping decreases it, adjustments are audited.
- Approval is an actual workflow (pending → approved/rejected), not a flag.
- Cross-module reactions go through a **transactional outbox**; no event is lost on a crash.
- Rate limiting, HTTPS/HSTS, security headers, and a CSP for the Blazor app are on.
- Observability: structured logs, traces and metrics via OpenTelemetry; `/health/live` and
  `/health/ready`; correlation ids in every log line.
- The app installs from the docs alone: Compose on Podman (Docker-compatible), migration bundles, first-run admin, backups.
- Sign-in keys (data protection) are encrypted at rest with a certificate in Production.

A lesson may still *start* from a simpler version for teaching — rule 2.7 — as long as a later phase
replaces it.

---

## 7. Pre-edit checklist

1. **Stack and licence:** does this stay inside §3 and add only permissively licensed packages?
2. **Logic placement:** does it keep the §4.6 split intact?
3. **Boundaries:** everything in a module `internal`; only `.Contracts` public; no module→module main-project
   reference; no cross-schema SQL?
4. **No phantom APIs:** is every type/method defined, registered, and already met by the reader?
5. **Why, briefly:** did I give the reason — in as few words as it takes?
6. **Runnable:** does the phase still end in something the reader can verify?
7. **Lesson ↔ build in sync:** if I changed one, did I change the other in the same edit, and do the build
   and tests still pass?
8. **Current APIs:** are `.NET 10` / EF Core 10 / package API claims current?
9. **Structure, analogies, callouts, voice** preserved (§2)?
10. **README.md and ROADMAP.md** updated if structure, conventions, or progress changed?

When in doubt, **ask or flag** rather than guess.
