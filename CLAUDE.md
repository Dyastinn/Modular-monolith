# CLAUDE.md — Rules for This Repository

## Start here

This repo is a **course** that builds a **real, open-source product**: **YASDA** (*Yet Another Service Desk
Application*). It has tickets, SLAs and a knowledge base. It is a **Modular Monolith with Vertical Slice
Architecture (VSA)**, with a Blazor front end. It is MIT-licensed ([LICENSE](LICENSE)). The lessons teach the
reader to build it, and the reference build *is* the product.

The five rules that matter most:

1. **Don't change the stack.** Use only the technologies in §3. Never add a banned one.
2. **Don't show the reader something they haven't learned.** Every API in a lesson is taught there or earlier.
3. **Lesson and build stay in sync.** Change one, change the other in the same edit.
4. **Done means production-ready.** No shortcuts left in the final product (§6).
5. **If a request breaks a rule here, stop and say so.** Don't silently break the course.

**This file wins.** If an older lesson or code file disagrees with this file, that file is out of date. Don't copy
it.

### Old projects: never bring them back

- **The old SQL Server tutorial.** Its leftovers: `usp_`, `rowversion`, `GO`, `[brackets]`, LocalDB,
  `Microsoft.Data.SqlClient`, IIS paths, `web.config`.
- **The ERP.** This project used to be a Philippine ERP (sales, inventory, purchasing, invoicing, BIR tax). It is
  kept only under the git tag `erp-final`. Its leftovers: `SalesOrder`, `Customer`, `Money`, `₱` amounts, `BIR`,
  `MudBlazor`, `Mud*`, the `Erp.` namespace.
- **Nothing about money comes back:** no prices, invoices, tax, fines or dues.
- If you see one of these leftovers, it's stale. Replace it; don't build on it.

Decisions and their dates are recorded in [ROADMAP.md](ROADMAP.md). This file states only the rules.

---

## 1. What's in the repo

| Path | What it is |
|---|---|
| `plan-business-tutorial/` | **The course.** The only one. Lessons run in order and each ends in something runnable. Also holds `00-START-HERE.md`, `GLOSSARY.md` and `ANSWER-KEY.md`. |
| `course-validation/business/` | **The reference build, which is the product** (`Yasda.slnx`). Every lesson's code must match it. |
| `DESIGN.md` | **How every screen looks.** |
| `WRITING-STYLE.md` | **How every lesson is written.** |
| `.claude/rules/blazor-ui.md` | How screens are built (plain Blazor, tables, forms, messages, browser tests). |
| `ROADMAP.md` | The plan: decisions, the milestone in progress, open risks. Update it as work lands. |
| `ROADMAP-HISTORY.md` | Finished milestones and the dated log. Open only for a specific question. |
| `README.md` | The front door. Keep it in sync with any structural change. |
| `LICENSE` | MIT, for YASDA. |

- **There is one reference build.** Don't create a second one.
- **There is one course.** Don't create or link to other course folders (`plan-beginner-friendly/`, `plan-Opus/`),
  and don't use their vocabulary (`INotification`, `Submit(bool approvalGranted)`, `tx.Inner`, `IPricingService`).
- **The course is written milestone by milestone.** A milestone is done only when its code, tests, lessons,
  answer-key entries and glossary terms are all written. ROADMAP.md says which lessons each milestone brings.

### How to work efficiently

- **Lessons are big (30–95 KB).** List a lesson's headings first, then read only the section you need.
- **In ROADMAP.md, read only the milestone in progress.**
- **While working, run only the test project you changed.** Run the whole suite once at the end.
- **Order of work for a milestone:** code and tests first → then the lessons, written from the working code →
  then follow the lessons in a scratch folder to prove they build and pass.

---

## 2. How the course teaches

The reader should end each lesson with working code and a clear reason for every piece of it, in as few words
as that takes. A lesson that skips the "why" has failed. So has a lesson that buries the point.

1. **Assume nothing, explain once.** Don't assume the reader knows DDD, CQRS, DI, `async`/`await`, EF Core or race
   conditions. Teach each concept once, in the lesson that first needs it, briefly. Later lessons link back.
2. **Build step by step.** Each lesson uses only what earlier lessons taught. Never mention an API before it's
   taught. Never say "as you know". Each lesson ends in something the reader can run and check.
3. **Introduce each thing only when it's used.** This applies to packages, tools, files, concepts and config
   options. Explain it in that lesson. Never add anything "for later". When a later lesson replaces something,
   show the before and after, and remove the old one. Details: [WRITING-STYLE.md](WRITING-STYLE.md).
4. **No phantom APIs.** Every type and method shown is defined where the reader has met it, or right there, with
   its DI registration. Nothing needed is "left as an exercise".
5. **Problem first, then the fix, briefly.** Show the simple version and why it hurts in a few lines, then the
   better one.
6. **Give the reason.** One or two sentences of *why* per decision.
7. **Be honest about costs.** Name each pattern's downside (EF change-tracking overhead, two data-access tools,
   one hand-written registration per handler, best-effort vs. outbox), in the voice: *"Now, let's be honest about
   the cost here: …"*
8. **Every lesson proves itself with tests.** From lesson 02 on, each lesson ends with tests for what it built:
   the tests that teach something new in full, then a table of every edge case (test name + why). Each kind of
   test is set up in the lesson that first needs it. Make each test fail once; a test that never failed proves
   nothing.
9. **Assume nothing is installed.** The lesson that first uses a tool teaches installing it on Windows, macOS,
   Fedora and Ubuntu, with a command that proves it worked. .NET, Git and an editor come in lesson 01, PostgreSQL
   in 05, a test browser in 06, Podman in 12. If a tool's license has conditions, say so and give the free
   alternative.
10. **Never end on a shortcut.** An early lesson may use a simpler version to teach. A later lesson must replace
    it and say so.

### The voice

**Before writing or editing any page in `plan-business-tutorial/`, read [WRITING-STYLE.md](WRITING-STYLE.md) in
full. Before calling the page done, go through its checklist.** A correct lesson in the wrong style is not done.

The short version: a friendly instructor talking to one learner, in English only. A greeting and "Are you ready?
Let's begin!" at the start, "you" and "let's", "Let's check!" questions with no answers shown (answers go in
ANSWER-KEY.md), and "Good luck, developer!" at the end. No `>` callouts, no emoji, no labels like "Note:".

- **Use these analogies, and no others for the same ideas:**

  | Idea | Analogy |
  |---|---|
  | Outbox dispatcher | A post office: it delivers each event to whoever subscribed; the sender never knows who |
  | Atomicity | A turnstile |
  | Modules | Islands with one service window (the Contracts project) |
  | VSA | A taco drawer |
  | Value object | Any ₱1,000 bill is as good as any other (the one allowed use of ₱) |

- **Use real names and numbers from this domain:** `Ticket`, `TeamLead`, "respond within 4 business hours".
- **Diagrams are Mermaid.** Use an image (in `plan-business-tutorial/images/`) only when Mermaid can't show it,
  such as a screenshot.
- **File references in prose are Markdown links**, not backticks or bare paths.

---

## 3. The stack (locked)

| Area | Use |
|---|---|
| Platform | .NET 10, C#, **Modular Monolith + VSA**, CQRS-lite |
| API | **Minimal APIs** (`MapGroup`, `TypedResults`). Each module maps its own endpoints. Each endpoint calls its handler directly. Everything is under **`/api`** |
| Database | **PostgreSQL** 16+ (developed on 18). One schema per module. snake_case names (`EFCore.NamingConventions`) |
| Writes | **EF Core (Npgsql)** for every write and every transaction |
| Reads | **Dapper + schema-qualified SQL functions**, SELECT only (`RETURNS TABLE`; PostgreSQL procedures can't return rows), shipped through EF Core migrations |
| Validation | **FluentValidation**, run by one endpoint filter on `/api` |
| Errors | **Stable error codes** on every problem response, with a catalog page |
| Auth | **ASP.NET Core Identity** (`AddIdentityApiEndpoints` / `MapIdentityApi`): cookies for the browser, bearer + refresh tokens for other clients, lockout, password policy, role-based authorization. Keys persisted to PostgreSQL |
| Front end | **Blazor Web App, no component library.** Plain HTML, Blazor's own forms, one CSS file of [DESIGN.md](DESIGN.md)'s tokens. Global interactive WebAssembly, prerender off, served by `Yasda.Api` |
| Observability | **OpenTelemetry** (traces, metrics, logs, OTLP export) + health checks. **Aspire AppHost** as the dev-only dashboard |
| API docs | **`Microsoft.AspNetCore.OpenApi`** + **Scalar** (dev-only page) |
| Time | `TimeProvider` (tests use `FakeTimeProvider`) |
| Events | **Transactional outbox** |
| Tests | **xUnit v3 on Microsoft.Testing.Platform + AwesomeAssertions**, **FsCheck.Xunit.v3**, **Testcontainers.PostgreSql** (from lesson 12), **coverlet.MTP**, **Stryker.NET** (`test-runner: mtp`), **GitHub Actions** CI |
| Analyzers | `latest-recommended`. Warnings fail the CI build |
| Email | **MailKit** (sending now, receiving tickets by email later) |
| Containers | **Podman** for the dev database (from lesson 12), tests and deployment. Linux containers, **Compose** files run with `podman compose`, Caddy for HTTPS |

More stack rules:

- **Container files stay Docker-compatible:** plain Compose spec, OCI `Dockerfile`, no Quadlet or other
  Podman-only format. Say once, where it matters, that Docker runs the same files.
- **Lessons 05–11 use a natively installed PostgreSQL on purpose.** Lesson 12 shows the pain, then moves to
  containers.
- **No IIS.** No IIS path, `web.config` or Windows-hosting lesson.
- **The screens are built along with the logic.** Lesson 01 creates the Blazor app, lesson 06 signs in through
  it, and every feature lesson adds its screen. No separate test console and no separate "interface" lesson.
- **No API keys.** The browser app signs in with Identity's cookie (a key shipped in a browser app is public).
  Add machine-client keys only when a non-person client exists.
- **Auth comes from libraries only.** Never hand-write JWT minting, refresh-token tables, password hashing or token
  rotation. If Identity can't do something, say so and pick another *library* (OpenIddict for SSO/OAuth).
- **`/auth/register` is blocked.** Admins create accounts.

### Licenses: free for everyone, forever

YASDA must never cost anything to run, change or redistribute. Whoever runs it inherits every dependency's
license.

- **Allowed:** permissive open-source licenses only (MIT, Apache-2.0, BSD, PostgreSQL).
- **Not allowed:** per-developer or per-seat commercial licenses, revenue-threshold "community" licenses, and
  copyleft licenses that would bind whoever runs it.
- **Known traps, all banned:** MediatR (paid since 2025), FluentAssertions ≥ 8 (paid), AutoMapper (paid since
  2025), MassTransit ≥ 9 (paid), QuestPDF (revenue-gated), PDFsharp/MigraDoc ≥ 6.5 (AGPL-3.0),
  Telerik/Syncfusion/DevExpress, Seq (commercial), Duende IdentityServer (revenue-gated).
- **Before adding any package:** check its license and record it in the third-party notices file.

### Banned in the built solution

- Clean Architecture's four-layer project split
- A mediator library
- A generic `Repository<T>`
- An `IUnitOfWork` wrapper over `DbContext` (the DbContext *is* the unit of work)
- EF Core for **reads** in query handlers (reads are Dapper)
- Dapper for **writes**
- Message brokers (RabbitMQ, Kafka, MassTransit)
- Redis or distributed caching
- Microservices
- AutoMapper
- Kubernetes or any orchestrator as a requirement (the target is Compose on one host)
- Docker Desktop as a requirement for servers (it's paid for larger companies; servers run the free Docker
  Engine, and the course uses Podman)
- Hand-written authentication or token code

A lesson may mention these only in a *"when you'd add this later, and why not now"* discussion.

---

## 4. Architecture rules (non-negotiable)

1. **A module is an island with one service window.**
   - A module is the project `Yasda.Modules.X`. **Everything in it is `internal`** except its one public entry
     class (e.g. `TicketsModule`).
   - When another module needs something from it, add `Yasda.Modules.X.Contracts`. That is **the only public
     surface**: contract interfaces, their DTOs and integration events. Don't create it before it's needed.
   - A module may reference **other modules' `.Contracts` projects only**, never another module's main project.
   - A Contracts project references only `Yasda.Shared.BuildingBlocks`. This keeps the reference graph free of
     cycles when modules call each other.
2. **No SQL across schemas.**
   - No `JOIN` and no foreign key across module schemas, not even to `identity.users` for a "created by" column.
     Store the id without an FK.
   - No DbContext maps another module's tables.
   - Data from another module comes through a contract call or an integration event.
3. **`internal` needs some framework workarounds. Keep them.**
   - A module's public surface is exactly two methods: `AddXModule()` (registers its DbContext, handlers,
     validators, event handlers and error catalog) and `MapXEndpoints()` (maps its endpoints).
   - Handlers are registered **by hand**, one line each. A test proves every one is registered.
   - `AddValidatorsFromAssembly` **must** pass `includeInternalTypes: true`. (.NET 10's built-in `AddValidation()`
     silently skips internal models. That's why we use FluentValidation.)
   - `InternalsVisibleTo` is granted **only to test assemblies**, not even to `Yasda.Api`.
   - Keep the tests that prove these: e.g. `A_user_without_a_name_is_rejected_by_the_internal_validator`,
     `Every_feature_handler_is_registered`, and the architecture test that fails if a module grants
     `InternalsVisibleTo` to anything but a test project.
4. **The Domain is pure.** `Domain/` depends only on `Yasda.Shared.BuildingBlocks`. No EF Core attributes, SQL,
   ASP.NET Core, Dapper or `HttpContext` in it. EF mapping lives in `Infrastructure/` as
   `IEntityTypeConfiguration<T>` (fluent API only).
5. **`Shared/` is exactly two projects.**
   - `Yasda.Shared.BuildingBlocks`: what every module may use. One flat namespace, folders `Domain/`,
     `Abstractions/`, `Messaging/`. Depends on nothing heavy. Don't split it: a project boundary exists to make
     something a compile error; folders just organize code.
   - `Yasda.Shared.Infrastructure`: the implementations. Referenced **only by `Yasda.Api`**.
6. **Where each kind of logic lives.** This is the core skill the course teaches.

   | Question | Where it goes |
   |---|---|
   | "Is the request well-formed?" (positive ids, a subject, a known priority) | **Validator** (FluentValidation) on the type the endpoint binds: a request in the endpoint file, or a query record in the handler file. Run by the `/api` filter. Answered 400 with `YASDA-0001`, no exception |
   | "Is this allowed?" (a status change, replying to a closed ticket, reopening too late, disabling yourself) | **Domain**: a method on the entity or value object, throwing `DomainException(XErrors.Code, detail)` → 422 with the code |
   | "Does the thing in the URL exist?" | **Handler** returns `null`/`false`. The **endpoint** answers `TypedResults.NotFound()`. An unknown id *in the body* is a coded 422 |
   | Load → do → save | **Command handler** (EF Core, no business `if`s) |
   | Reading data for a screen or report | **Query handler** → Dapper → SQL function |
   | Two people acting at once (two agents taking one ticket, lost updates) | **EF Core**: the `xmin` row version as a concurrency token, or one atomic `ExecuteUpdate` with a guard in the `WHERE` |
   | One module needs another, in the same transaction | **Synchronous contract** in the other module's `.Contracts` |
   | Something reacts afterward, in the same module | **Domain event** (internal), raised by the aggregate → `OutboxInterceptor` writes it in the same `SaveChanges` → `OutboxDispatcher` delivers it to the module's `IDomainEventHandler<T>` after commit. Delivery is at-least-once, so **handlers must be idempotent** |
   | Another module reacts afterward | The owning module's domain event handler maps it to a public **integration event** (in `.Contracts`) and calls `IIntegrationEventPublisher`. That hands it to every `IIntegrationEventHandler<T>` in the same outbox delivery |
   | "Only role X may call this endpoint" | **Endpoint** (`RequireAuthorization` / role policy) |
   | Access that depends on the data ("requesters see only their own tickets") | **Handler or domain**, via `ICurrentUser` |

   These are **defects**: a business `if` in a handler, a business rule in a validator or SQL function, a rule
   that needs a database to unit-test.

---

## 5. Conventions

### Features and handlers

- **Two files per feature, in one feature folder:**
  - `XxxHandler.cs`: the Command or Query, its Result/DTOs, the Handler, and the validator of a query bound with
    `[AsParameters]`.
  - `XxxEndpoint.cs`: the Request, its validator, and a static `Map` method.
  - Closely related actions on one thing may share a folder (`UpdateUser` also holds disable, enable and reset
    password).
  - **Never** use folders by technical layer (`Commands/`, `Handlers/`, `Validators/`, `Endpoints/`).
- **Handlers:**
  - Shape: `internal sealed class XxxHandler` with `Task<T> HandleAsync(XxxCommand or XxxQuery, CancellationToken)`.
  - Registered with `AddScoped` in the module, injected into the endpoint.
  - Commands and queries are separate records. A query handler never takes a DbContext; a command handler never
    uses Dapper (an architecture test checks this).
  - A command on a resource named in the URL returns `bool` or `T?` (false/null = not found).
  - Handlers never publish events themselves. The outbox does.
- **Endpoints** return `TypedResults` with declared `Results<…>` unions, so OpenAPI lists every answer.
- **Tracing** per request comes from ASP.NET Core's OpenTelemetry instrumentation.
- **Error codes:**
  - Every `DomainException` carries an `ErrorCode` from its module's internal catalog (e.g.
    `Domain/IdentityErrors.cs`: `IDENTITY-0001`…, with a Title and a Fix), registered with `ErrorCatalog.From`.
  - Codes are never renumbered or reused. Tests prove they are unique, documented and used.
  - Every problem response has `code`, `type: /errors/{code}` and `traceId`. `/api/errors` serves the catalog;
    the app's `/errors/{code}` page shows it.
- **Time:** inject `TimeProvider`. The host registers `CompanyTimeProvider`, whose local zone is the company's.
  Use `time.GetUtcNow().UtcDateTime` and `time.Today()`. Tests use `FakeTimeProvider`.

### Domain

- **Base types** in `Yasda.Shared.BuildingBlocks`: `Entity<TId>`, `AggregateRoot<TId>` (`where TId : struct`,
  repeated on derived classes), `IDomainEvent`, `DomainException`.
- **Strongly-typed IDs** (`readonly record struct TicketId(long Value)`) with EF value converters. Never a bare
  `long` id in the domain.
- **Rich domain:** private setters, a `Create()` factory plus a private parameterless constructor for EF, guarded
  state changes, and value objects (a ticket's subject, an SLA's business-hours duration) that reject invalid
  values.
- **A value object used in a domain event** needs `[JsonConstructor]` on its validating constructor. Otherwise the
  outbox round-trip silently zeroes it. (`An_event_survives_the_round_trip_through_the_outbox` catches this.)
- **Domain events:**
  - Raise an event only in the same change that adds its first handler.
  - Domain events are `internal`. Integration events live in a module's `.Contracts` project and are plain data.
  - Each module registers its own event handlers. Tests prove every event has a handler and every handler is
    registered.
- **Roles** are defined once in `Yasda.Shared.BuildingBlocks.Roles` (`Admin`, `TeamLead`, `Agent`, `KbEditor`,
  `Requester`) and seeded into `identity.roles` by migration. Every account has exactly one role and is created by
  an admin. Never type a role as a string.

### Data

- **Writes:**
  - The command handler uses **the module's DbContext directly**: load the aggregate, call domain methods,
    `SaveChangesAsync`. No per-aggregate repository.
  - One `SaveChangesAsync` is atomic on its own.
  - When a command also calls **another module's contract**, the transaction is **explicit in the handler**
    (`BeginTransactionAsync` … `CommitAsync`) and passed to the contract, which joins it (`UseTransaction`). A
    throw means rollback.
- **DbContexts:**
  - One per module, mapped to that module's schema, with its own migrations history table in that schema.
  - Every module DbContext in a request uses **the same `DbConnection`** (`ISharedConnection`). That's what lets a
    contract join the caller's transaction.
  - Each has a static `Configure(options, connection)`, used both at runtime and by its
    `IDesignTimeDbContextFactory`, so `dotnet ef` and the app can never disagree.
  - Table names are explicit and singular (`ToTable("ticket")`).
- **Concurrency:**
  - Each aggregate root has a shadow `Property<uint>("RowVersion").IsRowVersion().HasColumnName("xmin")`
    (PostgreSQL's built-in row version). `DbUpdateConcurrencyException` → 409.
  - If an aggregate's child rows can change on their own (a ticket's replies), saving must also touch the root
    row, or its version is never checked. (Tag `erp-final`, `SalesDbContext.SaveChangesAsync`, shows the pattern
    and its integration test.)
- **Reads:**
  - Query handlers call SQL functions with Dapper: `SELECT * FROM identity.search_users(@...)`.
  - Functions are **schema-qualified** in their definition and in every C# call.
  - One `.sql` file per read, in the module's `Infrastructure/Sql/Functions/`, named `schema.function.vN.sql`,
    with its companions (the `count_` for a search, the `_lines` for a get). Applied by a migration via
    `migrationBuilder.Sql`.
  - Dapper maps snake_case columns to PascalCase records (`MatchNamesWithUnderscores`, set once in
    `AddSharedInfrastructure`).
- **Migrations, in the lessons vs. in the build:**
  - **Lessons:** a shipped migration or script is never edited. A changed function gets a `.v2` file and a new
    migration, step by step.
  - **Reference build:** squashed (lesson 05). Each module has exactly one `InitialCreate` and one
    `AddReadFunctions`, and each function exists only as its final `.v1.sql`. To change the build, edit the
    `.v1.sql` and regenerate those two migrations. Never add a `.v2` file or another migration to the build,
    unless a lesson teaches that exact step.
  - Once the product is installed somewhere, this flips: released migrations freeze.
- **Dev seed data** lives in each module's `Infrastructure/Sql/DevSeed.sql`. It runs only when
  `Database:SeedDevelopmentData` is true (Development and tests) and is idempotent.
- **Least privilege in production:** the app connects as `yasda_app` (rows only). Migrations run as `yasda_owner`
  via `ConnectionStrings:YasdaDbMigrations` (see `deploy/postgres/create-roles.sql`).

### Business data

- **No money anywhere.** No prices, invoices, tax, fines or dues.
- **Business values are table rows, edited on screens.** SLA policies, business hours, holidays, categories,
  teams and routing rules are data an admin or team lead edits.
- **Configuration describes the installation only:** connection strings, certificates, SMTP, the time zone,
  telemetry, the first admin. Never put a deadline, an hour or a holiday in `appsettings.json` or `.env`.
- **Time is the hard part of this domain.** Deadlines are stored, never kept as timers in memory. They're computed
  in business hours, in the organization's time zone, with holidays as rows (Philippine holidays seeded,
  editable).

### Screens

**Before writing any `.razor` or `.css`, in the build or in a lesson, read [DESIGN.md](DESIGN.md) and
[.claude/rules/blazor-ui.md](.claude/rules/blazor-ui.md).** Never break these:

- Plain HTML and Blazor's own components. No component library.
- Every color, font and gap comes from `app.css`'s `:root` tokens.
- No inline styles (the CSP forbids them).
- Every page reads correctly with CSS off.
- A list's search, sort and page live in the address, and it sorts **on the server**.
- **Browser tests** find things by role and name and check with Playwright's `Expect`. Never CSS classes,
  `WaitForTimeout`, `NetworkIdle`, or read-once checks. If a control can't be found by role, fix its
  accessibility (a `<label>`, an `aria-label`); don't reach for CSS.
- **How the host serves the app:**
  - The host references `Yasda.Web` normally, with `Aliases="YasdaWeb" PrivateAssets="all"`.
  - Never `ReferenceOutputAssembly="false"`: the host would stop serving the app's CSS and logo, and the app would
    run unstyled.
  - `App.razor` and `Routes.razor` both live in `Yasda.Web`. The host maps
    `MapRazorComponents<YasdaWeb::Yasda.Web.App>().AddInteractiveWebAssemblyRenderMode().AllowAnonymous()` and sets
    `RequiresAspNetWebAssets` (it has no `.razor` files, so the SDK wouldn't serve `blazor.web.js` otherwise).

### Code

- **Keep code simple.** This is a course; a beginner reads every line. Plain `if`/`else`, sequential `await`s,
  plain null checks. No clever LINQ chains or pattern-matching tricks where a simple version reads as well.
- **Every dependency is free and permissively licensed** (§3), and listed in the third-party notices file.
- **Comments are minimal.** None on self-explanatory code. Where one is needed: one short line with the
  non-obvious *why* (a trap, a constraint). The lessons carry the explanations.

---

## 6. What "done" means

The finished product has **no deliberate shortcuts**:

- **SLA clocks survive a restart.** Deadlines are rows, a scheduler polling them finds breaches, and pausing
  (waiting on the requester) really stops the clock.
- **Requesters see only their own tickets**, proven by API tests, not just hidden on screen.
- **Attachments** are limited in size and type, stored outside the web root, and downloaded with safe headers.
- **Cross-module reactions go through a transactional outbox.** No event is lost on a crash.
- **Security:** rate limiting, HTTPS/HSTS, security headers, and a CSP for the Blazor app.
- **Observability:** structured logs, traces and metrics via OpenTelemetry; `/health/live` and `/health/ready`;
  correlation ids in every log line.
- **Installs from the docs alone:** Compose on Podman (Docker-compatible), migration bundles, first-run admin,
  backups.
- **Sign-in keys** (data protection) are encrypted at rest with a certificate in Production.

A lesson may *start* from a simpler version (rule 2.10), as long as a later lesson replaces it.

---

## 7. Checklist before every edit

1. **Stack and license:** does it stay inside §3, adding only permissively licensed packages?
2. **Logic placement:** does it keep the §4.6 table intact?
3. **Boundaries:** everything in a module `internal`, only `.Contracts` public, no module→module main-project
   reference, no cross-schema SQL?
4. **No phantom APIs:** is every type and method defined, registered, and already met by the reader?
5. **Why, briefly:** is the reason given, in as few words as it takes?
6. **Runnable:** does the lesson still end in something the reader can check?
7. **Lesson and build in sync:** if one changed, did the other change in the same edit, and do the build and tests
   pass? (Exception: work ROADMAP marks **build first, lessons after**. There the lesson stays listed as open in
   ROADMAP until written.)
8. **Current APIs:** are the .NET 10, EF Core 10 and package API claims current?
9. **Lesson style:** did you read [WRITING-STYLE.md](WRITING-STYLE.md) and pass its checklist?
10. **Docs:** are README.md and ROADMAP.md updated if structure, conventions or progress changed?

**When in doubt, ask or flag. Don't guess.**
