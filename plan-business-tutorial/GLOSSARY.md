# Glossary — Every Term in Plain English

Keep this open while you read the lessons. Terms are grouped by topic, so related ideas sit together. Each
one has a plain meaning, the lesson where it first appears, and sometimes an everyday picture to hold on to.

---

## The big architecture words

**Domain** (04)
The part of the code that's about the business: orders, customers, money, rules. Nothing about databases or
websites.

**Business rule** (04)
A statement about what the business allows. "Only drafts can be edited." "Orders over ₱50,000 need a
manager."

**Invariant** (04)
A rule that's always true about an object. "An order's total always equals the sum of its lines."

**Rich domain** (04)
Objects that enforce their own rules, so they can't be put into an illegal state. Its opposite, an *anemic*
domain, is bags of data whose rules live somewhere else and drift apart.

**Monolith** (01)
One program you deploy and run, as opposed to many separate services talking over a network.

**Modular monolith** (01)
One program, split inside into modules with real walls between them.

**Module** (01)
One business area (Sales, Inventory, Identity) with its own code, its own schema and its own DbContext.
Picture an island with one service window: everything inside is private, and other modules can only ask at
the window.

**Contracts project** (09)
A module's service window: `Erp.Modules.X.Contracts`, the only public part of a module. It holds the
interfaces and data other modules may use, and references nothing but BuildingBlocks, so modules can call
each other without forming a cycle.

**Vertical slice** (01)
Everything for one feature in one folder, usually two files: `XxxHandler.cs` and `XxxEndpoint.cs`. Picture a
taco drawer: everything for tacos in one drawer, instead of tortillas in one room and salsa in another.

**BuildingBlocks and Infrastructure** (01)
The two shared projects. `Erp.Shared.BuildingBlocks` is the shared vocabulary every module may use: base
classes and small interfaces. `Erp.Shared.Infrastructure` holds the real implementations, and only the host
references it.

**Host / composition root** (01)
`Erp.Api`: the one project that references everything and wires it together in `Program.cs`.

**`internal`** (01)
A C# keyword: visible inside this project only. Every type in a module is `internal` except its one entry
class, such as `SalesModule`.

**`InternalsVisibleTo`** (03)
Lets one named project see another's `internal` types. Granted only to test projects: never to another
module, and not even to the host.

## Building the solution

**`.slnx`** (01)
The solution file in its newer XML format. Lists the projects.

**`Directory.Build.props`** (01)
Settings every project in the folder shares, such as the target framework.

**Central package management** (01)
Every package version lives once, in `Directory.Packages.props`. Projects name a package without a version.

**Transitive pinning** (05)
Makes a version in `Directory.Packages.props` win even for packages you didn't reference directly, so a
patched version is used everywhere.

**Code analyzers** (01)
Rules the compiler checks on every build (`AnalysisLevel` in `Directory.Build.props`): a culture-dependent
`Parse`, a slow string comparison. A rule that doesn't fit is switched off in `.editorconfig`, with its reason.

## Commands, queries and handlers

**Command** (03)
A request to change something: `SubmitSalesOrderCommand`.

**Query** (03)
A request to read something, changing nothing: `SearchSalesOrdersQuery`.

**Handler** (03)
The plain class that does the work for one command or query, with one `HandleAsync` method. The endpoint
asks for it by name and calls it.

**CQRS-lite** (03)
Treating commands and queries differently, in one database. Here: writes through EF Core, reads through
Dapper and SQL functions.

**Mediator** (03)
A library that delivers each command or query to its handler, wrapped in shared steps. Common in .NET, but
not used here: our requests only arrive over HTTP, where an endpoint filter does the same job. Lesson 03
explains when you'd want one.

**Endpoint filter** (03)
Code that runs around an endpoint. Our one validation filter, on the whole `/api` group, checks every
request before its endpoint runs.

**Validator** (03)
A FluentValidation class that checks a request's *shape*: positive ids, at least one line. Never business
rules. It checks the type the endpoint receives: a request body, or a query read from the address.

## The web edge

**Blazor Web App** (01)
The current shape of a Blazor app: the server sends the page (`App.razor`), and the screens run where you
choose. Ours all run in the browser as **WebAssembly**, downloaded once and served by the API itself.

**MudBlazor** (01)
The free component library for the screens: tables, forms, dialogs, menus.

**Razor component** (01)
A `.razor` file: markup on top, C# in `@code` below. Pages, dialogs and the layout are all components.

**`AuthenticationStateProvider`** (06)
The class Blazor asks "who's signed in?". Ours asks the API's `/api/auth/me`.

**Minimal API** (03)
ASP.NET Core's lightweight way to map a URL to a function: `app.MapPost("/sales-orders", ...)`.

**`TypedResults`** (03)
Answers with a real type (`Ok<T>`, `NotFound`, `Created<T>`), so tests can check them and the API description
lists every answer an endpoint can give. Microsoft prefers them to `Results`.

**OpenAPI** (07)
A machine-readable description of every endpoint, at `/openapi/v1.json`. Scalar shows it as a page at
`/scalar`, in development.

**Middleware** (07)
A step every request passes through, in order: exception handling, HTTPS, headers, sign-in, rate limits.

**Problem Details** (07)
The standard JSON shape for errors (RFC 9457): `status`, `title`, `detail`, plus our `code`, `type` and
`traceId`.

**Error code** (04, 07)
A stable name for a kind of problem, like a compiler's `CS0122`: `SALES-0003` is always "the order would
exceed the credit limit". Each module keeps its codes, with a fix for each, in one catalog
(`Domain/SalesErrors.cs`); the app's Error codes page shows them. A code never changes meaning.

**Trace id** (14)
The id of *one* request, in every log line and error it produced. The code says what kind of problem; the
trace id finds this one in the logs.

**Status codes we use** (07)
400 malformed request, 401 not signed in, 403 not allowed, 404 not found, 409 someone else changed it first,
422 breaks a business rule, 429 too many requests, 500 our bug.

## Domain building blocks

**Entity** (04)
An object with an identity that lasts: customer 42 is still customer 42 after a name change.

**Value object** (04)
An object defined only by its value, and impossible to create invalid: `Money`, `Quantity`. Any ₱1,000
bill is as good as any other.

**Aggregate / aggregate root** (04)
A group of objects saved and changed together, with one entry point that guards the rules. `SalesOrder` is
the root; its lines can only change through it.

**Strongly-typed id** (04)
`readonly record struct CustomerId(long Value)` instead of a bare `long`, so the compiler stops you mixing up
ids.

**Factory method** (04)
A static `Create(...)` that checks the rules before an object exists. The object's constructor stays
private.

**`DomainException`** (04)
Thrown when a business rule says no, with the rule's error code. The API turns it into a 422.

**Domain event** (13)
Something the domain says has happened, in its own module's words: `SalesOrderApproved`. Internal: only its
own module handles it, in an `IDomainEventHandler<T>`.

**Integration event** (17)
What a module tells *other* modules, like `ItemCatalogChanged`. Public, in the Contracts project, with plain
types. The owning module maps one of its domain events to it, so its domain can change without breaking
anyone listening.

**Snapshot** (04, 20)
A copy of facts as they were at a moment: an order line's item name, an invoice's customer address. Later
changes don't touch it.

**Segregation of duties** (04)
Nobody approves their own order or purchase: spending needs a second pair of eyes.

**Unit of measure** (04)
How an item is counted: whole pieces, or kilograms that allow 2.5.

## Database words

**PostgreSQL** (05)
The database. Free, open source, runs on Windows and Linux.

**Schema** (05)
A named group of tables inside one database: `sales`, `inventory`, `identity`. One per module.

**snake_case** (05)
`sales_order`, `customer_id`: PostgreSQL's naming style. EF Core translates from C#'s PascalCase.

**EF Core** (05)
Microsoft's object-relational mapper. Here it does every write and every transaction.

**DbContext** (05)
EF Core's gateway to the database for one module. It tracks the objects you loaded and saves all their
changes in one go, so it already is the unit of work; we don't wrap it in repositories.

**Change tracking** (05)
EF Core remembers what each loaded object looked like, and `SaveChangesAsync` writes only what changed.

**Value converter** (05)
Tells EF Core how to store a type it doesn't know: `CustomerId` as a `bigint`, `Money` as a `numeric`.

**Migration** (05)
A C# file describing one change to the schema. EF Core records which ones each database has had.

**Migration bundle** (24)
One `.exe` containing a module's migrations. Run it with a connection string and it applies whatever is
missing.

**Seed data** (05)
Sample rows for development: `DevSeed.sql`, run only when `Database:SeedDevelopmentData` is on.

**Transaction** (09)
A group of changes that all happen or none do. Picture a turnstile: either you're through, or you're not.
There's no halfway.

**Atomic** (09)
Happens completely or not at all, with no in-between state anyone can see.

**Shared connection** (09)
One database connection per request, used by every module's DbContext, so a contract can join the
caller's transaction.

**Gap-free numbering** (20)
Invoice numbers from a counter row updated inside the invoice's transaction, so a rollback gives the number
back.

**Filtered unique index** (20)
Unique only among some rows: one *live* invoice per shipment, however many void ones.

**VATable / zero-rated / VAT-exempt** (20)
How a sale is treated for VAT. VATable: the price includes VAT (12% today). Zero-rated: VAT applies at 0%, as
for exports. VAT-exempt: no VAT at all, as for some farm products. The invoice shows each total separately.

**Tax regime** (20)
How the seller is registered: VAT, non-VAT under percentage tax, or exempt from both. It decides what the
invoice prints.

**RR 7-2024** (20)
The BIR's revenue regulation, under the Ease of Paying Taxes Act, that lists what an invoice must show.

**Company profile** (20)
The one row that holds the seller's registered name, TIN, address, tax regime, VAT rate and BIR permit. Business
data, kept by the accountant on a screen, not in `appsettings.json`.

**Shadow property** (20)
A column EF Core maps without a C# property on the entity, like an invoice's print count.

**Type handler** (19)
Teaches Dapper one type once, for every query: `date` columns become `DateOnly`.

**BIR Form 2307** (21)
The certificate a customer gives for the tax they withheld from a payment to us. The business claims it
against its own income tax, so a missing one is money lost.

**Creditable withholding tax** (21)
The small part of a payment (often 1% for goods, 2% for services, before VAT) a customer pays to the BIR on our
behalf. It settles the invoice like money does.

**`FULL JOIN`** (21)
Keeps rows from both sides, matched where they can be: withholding with no certificate yet, and a certificate
with no withholding.

**Special discount** (22)
A discount the law gives a person: senior citizens and PWDs pay no VAT on what qualifies, then 20% less.
Worked out once, in BuildingBlocks, so the order and the invoice always agree.

**Common table expression (CTE)** (21)
A named step inside a query, written with `WITH`. The aging report uses one.

**Sequence** (07)
A database counter that hands out each number once, even under load: the order numbers.

**Race condition** (09)
A bug that only shows when two things happen at the same moment, like two people buying the last item.

**`FOR UPDATE`** (09)
Locks the rows a query reads until the transaction ends. Anyone else who wants them waits.

**Lost update** (10)
Two people change the same thing; the second save silently overwrites the first.

**Optimistic concurrency** (10)
Don't lock; check on save that nobody changed the row since you read it. If someone did, refuse with 409.

**`xmin`** (10)
PostgreSQL's built-in row version, changed on every update. EF Core uses it as the concurrency token.

**Dapper** (08)
A small library that runs SQL and maps the rows to C# objects. Here it does every read.

**SQL function** (08)
A named query stored in the database, called like `SELECT * FROM sales.search_sales_orders(...)`. We use
functions because PostgreSQL procedures can't return rows.

**Least privilege** (23)
Give each account only the rights it needs. The app connects as `erp_app` (read and write rows); only
deployments use `erp_owner` (change tables).

**Default privileges** (23)
`ALTER DEFAULT PRIVILEGES`: rights given automatically on everything a role creates from now on.

## Events and the outbox

**Eventual consistency** (17)
A copy that catches up a moment later, through events. Fine for names; never for stock.

**Upsert** (17)
Insert if missing, update if present. Applying the same full record twice gives the same result.

**Inbox** (21)
The receiving side remembering what it already handled, like Sales' `applied_payment` table.

**Outbox** (13)
A table where each module writes its events in the same transaction as the change, so an event can't be
lost or sent for something that never happened.

**Interceptor** (13)
Code EF Core runs around `SaveChanges`. Ours copies domain events into the outbox.

**Dispatcher** (13)
A background service that reads the outbox and hands each event to its handlers. Picture a post office: the
aggregate writes the letter, the outbox is the mailbox, the dispatcher delivers it to whoever subscribed, and
the sender never knows who.

**At-least-once delivery** (13)
Every event is delivered, possibly more than once.

**Idempotent** (13)
Doing it twice has the same effect as doing it once. Every event handler must be.

**`SKIP LOCKED`** (13)
Lets two dispatchers share the outbox: each skips rows the other has locked.

**`BackgroundService` / `IHostedService`** (13, 23)
Code ASP.NET Core starts with the app. A `BackgroundService` keeps running (the dispatcher); an
`IHostedService` can simply do one thing at startup (the first admin).

## Sign-in and security

**Authentication / authorization** (06)
Who are you? / What may you do?

**ASP.NET Core Identity** (06)
Microsoft's sign-in library: users, roles, password hashing, lockout, cookies, tokens. We write no sign-in
code ourselves.

**Password hash** (06)
A one-way, deliberately slow scramble of a password. The database never stores the password itself.

**Lockout** (06)
After 5 wrong passwords, the account refuses sign-in for 15 minutes.

**Claim** (06)
One fact about the signed-in user, such as their id or role.

**Bearer token / refresh token** (06)
The access token proves who you are for a short while; the refresh token buys a new one. For apps that
aren't browsers.

**Cookie** (06)
How a browser stays signed in. The browser sends it automatically.

**Security stamp** (06)
A random value stored with each user and copied into their tokens. Changing it ("sign out everywhere", a new
password) ends every session.

**`ICurrentUser`** (06)
The small interface handlers use to ask who's calling.

**Role** (04, 16)
`SalesRep`, `SalesManager`, `Admin`, and from lesson 16 `InventoryClerk`, `Purchaser`, `Accountant`. Each
person has exactly one.

**Rate limiting** (23)
A budget of requests per caller per minute. Over it, the API answers 429.

**HTTPS / HSTS** (23)
HTTPS encrypts the connection. HSTS tells the browser to never use plain HTTP for this site again.

**Security headers** (23)
Response headers that tell the browser to be careful: no framing, no guessing content types.

**CSP (Content Security Policy)** (23)
A header listing what a page may load. For our JSON responses: nothing at all. For the app's pages: only
files from our own site, and never an inline script.

**CORS** (23)
The browser rule that stops a web page on one site calling an API on another, unless the API allows it.

**API key** (06)
A secret per client *program*. We use none: a key shipped in a browser app is public. Every call is protected
by who's signed in.

**Data protection keys** (23)
The keys Identity uses to protect cookies and tokens. Stored in the database so restarts sign nobody out,
and encrypted with a certificate so a stolen backup can't forge a sign-in.

**Open redirect** (06)
A sign-in page that sends you anywhere after signing in, including an attacker's look-alike site. Ours only
follows paths inside the app.

## Observability

**Observability** (14)
Being able to tell what the running app is doing, from the outside.

**Logs, traces, metrics** (14)
Logs: what happened. Traces: where the time went in one request. Metrics: how much and how often, over time.

**Span** (14)
One timed step inside a trace: the HTTP request, the handler, one SQL query.

**Trace id** (14)
One id shared by every span and log line of a request. Returned as `X-Trace-Id`.

**OpenTelemetry / OTLP** (14)
The open standard for producing telemetry, and the protocol for sending it anywhere.

**Health checks** (14)
`/health/live`: is the process up? `/health/ready`: can it reach the database?

**Aspire dashboard** (14)
A development tool that shows logs, traces and metrics live. Not for production.

## Testing

**Test** (02)
A check written down once and run forever. Arrange, act, assert.

**Unit test** (03)
Tests one class or rule alone, with no database or web server. Fast. *Picture: one bolt, checked on the
workbench.*

**Integration test** (02)
Tests pieces working together: here, the whole API run in memory and sent real requests; from lesson 05,
against a real PostgreSQL database. *Picture: the engine, running on a stand.*

**End-to-end test** (02, 06)
Tests the app the way a user uses it: a real browser clicking through the real screens of the running app.
Ours are the browser tests. *Picture: a test drive.*

**Test pyramid** (02)
The shape of a healthy suite: many fast unit tests, fewer integration tests, a handful of end-to-end tests.
Upside down, mostly slow browser tests, it's called the *ice-cream cone*.

**Manual / exploratory testing** (02)
A person checking by hand. Exploratory testing is using the app freely, looking for surprises no test asked
about. Every lesson's "Let's run it!" is one.

**Functional / non-functional test** (02)
Functional: does it do the right thing (the right total)? Non-functional: does it do it well enough (fast,
secure, under load)?

**Smoke test** (02)
A few quick checks that the app starts and its front page loads. If they fail, nothing else is worth running.

**`WebApplicationFactory`** (02)
Starts the API inside a test without a web server.

**`[Fact]` / `[Theory]`** (02)
xUnit's marks for a test, and for one test run with several sets of inputs (`[InlineData]`).

**Edge case** (02)
An input or moment at a boundary: empty, zero, exactly at a limit, one step past, twice, at the same instant,
someone else's data. Where bugs live.

**Regression test** (02)
Any test rerun after a change, to prove nothing that worked before broke (in practice, the whole suite). Also
a test written right after fixing a bug, so that bug can't come back.

**Test fixture / collection** (02, 05)
Something xUnit creates once and shares between tests, like the app; a collection makes classes share one
fixture and run one after another.

**AwesomeAssertions** (02)
The readable `.Should().Be(...)` assertions. A free fork of FluentAssertions, whose newer versions are paid.

**Architecture test** (03)
A test that fails when the code breaks a structural rule, such as a module referencing another module's main
project or a domain class having a public setter.

**Property-based test** (04)
States a rule that must hold for every input, and lets a library (FsCheck) try a hundred generated ones,
shrinking any failure to the simplest input.

**Playwright** (06)
Drives a real browser from a test: open a page, type, click, wait for text.

**Race test** (09)
Starts two actions at the same instant (`Task.WhenAll`), each with its own connection, to prove only the
right one wins.

**Code coverage** (11)
Which lines and branches the tests ran. Says what's untested; can't say what's tested *well*.

**Mutation testing** (11)
Changes the code on purpose, one small change (a *mutant*) at a time, and checks that some test fails. A
mutant that survives is a rule without a test. Stryker.NET does it.

**Testcontainers** (12)
A library that starts a container (our PostgreSQL) from inside the tests and removes it afterwards.

**CI (continuous integration)** (12)
A server builds and tests every change as soon as it's pushed. Ours is GitHub Actions.

## Deployment

**Container / image** (12)
An image is a program with everything it needs to run, frozen into one file. A container is a running copy of
it, isolated from the others.

**Podman** (12)
Runs containers, rootless and without a background service; free for any use. Runs the same images as Docker.

**Rootless** (12)
Containers that run with an ordinary user's rights, never root's.

**Compose** (24)
Runs several containers together on one machine from one file, in the right order. `podman compose` and
`docker compose` read the same file.

**Volume** (12)
Storage that outlives a container, so replacing the database container never touches the data.

**Caddy / reverse proxy** (24)
A web server in front of the app that takes care of HTTPS and passes requests on.

**Forwarded headers** (24)
`X-Forwarded-For` and `X-Forwarded-Proto`: how a proxy tells the app the real client address and scheme.

**Framework-dependent publish** (24)
A published app that uses the .NET runtime installed on the server, rather than carrying its own.

**`pg_dump` / `pg_restore`** (24)
PostgreSQL's tools to back up a database to one file, and to restore it.
