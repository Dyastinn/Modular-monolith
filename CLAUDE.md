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
| `runbooks/` | Operational runbooks (deployment to specific environments). |
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
6. **Be honest about costs.** Name each pattern's downsides (mediator indirection, EF change-tracking
   overhead, two data-access tools, best-effort vs. outbox), in the voice: *"Now, let's be honest about the
   cost here: …"*
7. **Production means production.** The final build has no "known shortcut" left in it. If an early phase
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
- **Consistent analogies** — mediator = *post office*; atomicity = *turnstile*; modules = *islands with one
  service window* (the contracts project); VSA = *taco drawer*; value object = *any ₱1,000 bill is as good
  as any other*. Don't invent competing ones.
- **Diagrams:** Mermaid blocks. An image (in `plan-business-tutorial/images/`) only when Mermaid can't
  express it, e.g. a UI screenshot.
- **File references in prose use Markdown links**, not backticks or bare paths.

---

## 3. The locked stack

.NET 10 · C# · **Modular Monolith + VSA** · **Carter** over minimal APIs · **PostgreSQL** (16+; developed on
18), schema-per-module, snake_case names (`EFCore.NamingConventions`) · **EF Core (Npgsql) for every write and
every transaction** · **Dapper + schema-qualified SQL functions for reads (SELECTs only)** — PostgreSQL
procedures can't return result sets, so the read side is `RETURNS TABLE` functions — **shipped through EF Core
migrations** · **`martinothamar/Mediator`** (source-generated, MIT) · **CQRS-lite** · **FluentValidation** ·
**ASP.NET Core Identity** (`AddIdentityApiEndpoints` / `MapIdentityApi`): cookies for the browser, bearer +
refresh tokens for other clients, lockout, password policy, role-based authorization; keys persisted to
PostgreSQL · **Blazor WebAssembly + MudBlazor**, hosted by `Erp.Api`, built along the way (every feature lesson
adds its screen) · the API lives under **`/api`** · **OpenTelemetry** (traces, metrics, logs, OTLP export) +
health checks; **Aspire AppHost** as the dev-only dashboard · **transactional outbox** · **xUnit +
AwesomeAssertions** · **PdfSharp/MigraDoc** for invoices · **MailKit** for email · deployment: **Docker**
(Linux containers, Docker Compose, Caddy for HTTPS) as the main path, **IIS** as the documented alternative.

> **Direction update (2026-11-06, user decision).** The product runs on Docker; the interface is built in the
> course alongside the logic: lesson 01 creates the Blazor app, lesson 05 signs in through it, and every
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
run the free Docker Engine) · hand-written authentication/token code (see above).

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
3. **`internal` has framework consequences — keep the fixes.** Carter's `AddCarter()` only scans public
   types, so each module **hand-registers** its `ICarterModule`s; `AddValidatorsFromAssembly` **must** pass
   `includeInternalTypes: true`; EF configurations and the Mediator source generator must be checked
   against internal types. `InternalsVisibleTo` is granted **only to test assemblies and to `Erp.Api`** —
   the Mediator source generator runs in the host and emits code that names each module's internal
   commands and handlers (verified 2026-11-05: without it, `CS0122`). **Never to another module**, and
   `Erp.Api` uses that access only for composition, never for business code. Keep the regression tests that
   prove each of these (e.g. `Order_with_no_lines_is_rejected_by_the_internal_validator`) and the
   architecture test that fails if a module grants `InternalsVisibleTo` to another module.
4. **Keep the Domain pure.** `Domain/` depends only on `Erp.Shared.BuildingBlocks`. No EF Core attributes,
   SQL, Carter, Dapper, or `HttpContext` in the domain. EF mapping lives in `Infrastructure/` as
   `IEntityTypeConfiguration<T>` (fluent API only).
5. **`Shared/` is exactly two projects.** `Erp.Shared.BuildingBlocks` (one flat namespace; folders
   `Domain/`, `Abstractions/`, `Messaging/`) depends on nothing heavy and holds what every module may use.
   `Erp.Shared.Infrastructure` holds implementations and is referenced **only by `Erp.Api`**. Don't re-split
   `BuildingBlocks` — *a project boundary exists to make something a compile error; folders organise code
   you're allowed to use.*
6. **Where each kind of logic lives** — the core skill the course teaches. Keep it sacred:

   | Concern | Home |
   |---|---|
   | "Is the request well-formed?" (shape: positive ids, ≥1 line) | **Validator** (FluentValidation, in the handler file) |
   | "Is this operation allowed?" (blocked customer, credit limit, status transition, approval threshold) | **Domain** — a method on the entity/value object |
   | "Load → do → save" coordination | **Command handler** (EF Core, no business `if`s) |
   | Reading data for a screen or report | **Query handler** → Dapper → SQL function |
   | Safety when two people act at once (reserve stock, lost-update guard) | **EF Core** — the `xmin` row version as a concurrency token, or one atomic `ExecuteUpdate` with a guard in the `WHERE` |
   | One module needs another (must be same transaction) | **Synchronous contract** in the other module's `.Contracts` |
   | Something reacts afterward | **Domain event** raised by the aggregate → `OutboxInterceptor` writes it in the same `SaveChanges` → `OutboxDispatcher` delivers it to `INotificationHandler<T>` after commit. At-least-once: **handlers must be idempotent** |
   | "This endpoint is role-X only, full stop" | **Endpoint** (`RequireAuthorization` / role policy) |
   | Auth that depends on the data ("your own customers only") | **Handler/domain** via `ICurrentUser` |

   A business `if` in a handler, a business rule in a validator or stored procedure, or a rule that needs a
   database to unit-test is a **defect**.

---

## 5. Conventions to preserve

- **Two files per feature:** `XxxHandler.cs` (Command/Query + Result/DTOs + Validator + Handler) and
  `XxxEndpoint.cs` (Request/Response + Carter module). Closely related actions on one thing may share a
  feature folder (`UpdateCustomer` also holds block/unblock; `ItemCategories` holds list and save). **Never**
  technical-layer folders (`Commands/`, `Handlers/`, `Validators/`, `Endpoints/`).
- **Mediator:** `martinothamar/Mediator` — `ICommand<T>`/`ICommandHandler<,>`, `IQuery<T>`/`IQueryHandler<,>`;
  pipeline = `TelemetryBehavior` (span + duration metric + log, outermost) then `ValidationBehavior`.
  Handlers return `ValueTask`. Handlers never publish events themselves — the outbox does.
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
- **Domain events:** raise an event only in the same change that adds its first handler. Integration
  events live in a module's `.Contracts` project, and **every Contracts assembly that holds events must be
  listed in the Mediator's `options.Assemblies`**, or publishing reaches no handler, silently. The Mediator
  generator's MSG0005 ("no handler") stays ON — never suppress it globally.
- **Dev seed data** lives in each module's `Infrastructure/Sql/DevSeed.sql`, runs only when
  `Database:SeedDevelopmentData` is true (Development/tests), and is idempotent.
- **Roles** live once in `Erp.Shared.BuildingBlocks.Roles` (`SalesRep`, `SalesManager`, `Admin`; later
  `InventoryClerk`, `Purchaser`, `Accountant`) and are seeded into `identity.roles` by migration. Staff have
  exactly one role. Never type a role string.
- **Least privilege in production:** the app connects as `erp_app` (rows only), migrations run as `erp_owner`
  via `ConnectionStrings:ErpDbMigrations` (see `deploy/postgres/create-roles.sql`).
- **Money is Philippine Pesos (₱)**, `numeric(18,2)`. Quantities are whole numbers today; they become
  `numeric(18,3)` when items gain units of measure (ROADMAP M3).
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
- The app installs from the docs alone: Docker Compose (or the IIS alternative), migration bundles, first-run
  admin, backups.
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
