# Glossary — Every Term in Plain English

Keep this open while you read the lessons. Terms are grouped by topic, so related ideas sit together. Each
one has a plain meaning, the lesson where it first appears, and sometimes an everyday picture to hold on to.

---

## The big architecture words

**Domain** (03)
The part of the code that's about the business: orders, customers, money, rules. Nothing about databases or
websites.

**Business rule** (03)
A statement about what the business allows. "Only drafts can be edited." "Orders over ₱50,000 need a
manager."

**Invariant** (03)
A rule that's always true about an object. "An order's total always equals the sum of its lines."

**Rich domain** (03)
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

**Contracts project** (08)
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

**`InternalsVisibleTo`** (02)
Lets one named project see another's `internal` types. Granted only to `Erp.Api`, where the mediator's
generator runs, and to test projects. Never to another module.

## Building the solution

**`.slnx`** (01)
The solution file in its newer XML format. Lists the projects.

**`Directory.Build.props`** (01)
Settings every project in the folder shares, such as the target framework.

**Central package management** (01)
Every package version lives once, in `Directory.Packages.props`. Projects name a package without a version.

**Transitive pinning** (04)
Makes a version in `Directory.Packages.props` win even for packages you didn't reference directly, so a
patched version is used everywhere.

**Source generator** (02)
Code that runs during the build and writes more code. The mediator uses one, so there's no reflection at
runtime.

## Commands, queries and the mediator

**Command** (02)
A request to change something: `SubmitSalesOrderCommand`.

**Query** (02)
A request to read something, changing nothing: `SearchSalesOrdersQuery`.

**Handler** (02)
The class that does the work for one command or query.

**CQRS-lite** (02)
Treating commands and queries differently, in one database. Here: writes through EF Core, reads through
Dapper and SQL functions.

**Mediator** (02)
Delivers each command or query to its handler, so the endpoint never knows which class does the work.
Picture a post office: the endpoint addresses the envelope, the mediator delivers it, the handler opens it.
We use `martinothamar/Mediator`.

**Pipeline behavior** (02)
Code that wraps every handler, like a filter: `TelemetryBehavior` (timing, tracing, logging), then
`ValidationBehavior`.

**Validator** (02)
A FluentValidation class that checks a request's *shape*: positive ids, at least one line. Never business
rules.

## The web edge

**Blazor WebAssembly** (01)
C# running in the browser. Our screens are a Blazor app, downloaded once and served by the API itself.

**MudBlazor** (01)
The free component library for the screens: tables, forms, dialogs, menus.

**Razor component** (01)
A `.razor` file: markup on top, C# in `@code` below. Pages, dialogs and the layout are all components.

**`AuthenticationStateProvider`** (05)
The class Blazor asks "who's signed in?". Ours asks the API's `/api/auth/me`.

**Minimal API** (02)
ASP.NET Core's lightweight way to map a URL to a function: `app.MapPost("/sales-orders", ...)`.

**Carter** (02)
A small library that lets each feature keep its endpoints in its own class (`ICarterModule`).

**Middleware** (06)
A step every request passes through, in order: exception handling, HTTPS, headers, sign-in, rate limits.

**Problem Details** (06)
The standard JSON shape for errors (RFC 9457): `status`, `title`, `detail`, plus our `traceId`.

**Status codes we use** (06)
400 malformed request, 401 not signed in, 403 not allowed, 404 not found, 409 someone else changed it first,
422 breaks a business rule, 429 too many requests, 500 our bug.

## Domain building blocks

**Entity** (03)
An object with an identity that lasts: customer 42 is still customer 42 after a name change.

**Value object** (03)
An object defined only by its value, and impossible to create invalid: `Money`, `Quantity`. Any ₱1,000
bill is as good as any other.

**Aggregate / aggregate root** (03)
A group of objects saved and changed together, with one entry point that guards the rules. `SalesOrder` is
the root; its lines can only change through it.

**Strongly-typed id** (03)
`readonly record struct CustomerId(long Value)` instead of a bare `long`, so the compiler stops you mixing up
ids.

**Factory method** (03)
A static `Create(...)` that checks the rules before an object exists. The object's constructor stays
private.

**`DomainException`** (03)
Thrown when a business rule says no. The API turns it into a 422.

**Domain event** (11)
Something the domain says has happened: `SalesOrderApproved`. Other code can react to it afterwards.

**Integration event** (16)
A domain event other *modules* handle, like `ItemCatalogChanged`. Public, in the Contracts project, with
plain types.

**Snapshot** (03, 19)
A copy of facts as they were at a moment: an order line's item name, an invoice's customer address. Later
changes don't touch it.

**Segregation of duties** (03)
Nobody approves their own order or purchase: spending needs a second pair of eyes.

**Unit of measure** (03)
How an item is counted: whole pieces, or kilograms that allow 2.5.

## Database words

**PostgreSQL** (04)
The database. Free, open source, runs on Windows and Linux.

**Schema** (04)
A named group of tables inside one database: `sales`, `inventory`, `identity`. One per module.

**snake_case** (04)
`sales_order`, `customer_id`: PostgreSQL's naming style. EF Core translates from C#'s PascalCase.

**EF Core** (04)
Microsoft's object-relational mapper. Here it does every write and every transaction.

**DbContext** (04)
EF Core's gateway to the database for one module. It tracks the objects you loaded and saves all their
changes in one go, so it already is the unit of work; we don't wrap it in repositories.

**Change tracking** (04)
EF Core remembers what each loaded object looked like, and `SaveChangesAsync` writes only what changed.

**Value converter** (04)
Tells EF Core how to store a type it doesn't know: `CustomerId` as a `bigint`, `Money` as a `numeric`.

**Migration** (04)
A C# file describing one change to the schema. EF Core records which ones each database has had.

**Migration bundle** (23)
One `.exe` containing a module's migrations. Run it with a connection string and it applies whatever is
missing.

**Seed data** (04)
Sample rows for development: `DevSeed.sql`, run only when `Database:SeedDevelopmentData` is on.

**Transaction** (08)
A group of changes that all happen or none do. Picture a turnstile: either you're through, or you're not.
There's no halfway.

**Atomic** (08)
Happens completely or not at all, with no in-between state anyone can see.

**Shared connection** (08)
One database connection per request, used by every module's DbContext, so a contract can join the
caller's transaction.

**Gap-free numbering** (19)
Invoice numbers from a counter row updated inside the invoice's transaction, so a rollback gives the number
back.

**Filtered unique index** (19)
Unique only among some rows: one *live* invoice per shipment, however many void ones.

**VATable / zero-rated / VAT-exempt** (19)
How a sale is treated for VAT. VATable: the price includes VAT (12% today). Zero-rated: VAT applies at 0%, as
for exports. VAT-exempt: no VAT at all, as for some farm products. The invoice shows each total separately.

**Tax regime** (19)
How the seller is registered: VAT, non-VAT under percentage tax, or exempt from both. It decides what the
invoice prints.

**RR 7-2024** (19)
The BIR's revenue regulation, under the Ease of Paying Taxes Act, that lists what an invoice must show.

**Company profile** (19)
The one row that holds the seller's registered name, TIN, address, tax regime, VAT rate and BIR permit. Business
data, kept by the accountant on a screen, not in `appsettings.json`.

**Shadow property** (19)
A column EF Core maps without a C# property on the entity, like an invoice's print count.

**Type handler** (18)
Teaches Dapper one type once, for every query: `date` columns become `DateOnly`.

**BIR Form 2307** (20)
The certificate a customer gives for the tax they withheld from a payment to us. The business claims it
against its own income tax, so a missing one is money lost.

**Creditable withholding tax** (20)
The small part of a payment (often 1% for goods, 2% for services, before VAT) a customer pays to the BIR on our
behalf. It settles the invoice like money does.

**`FULL JOIN`** (20)
Keeps rows from both sides, matched where they can be: withholding with no certificate yet, and a certificate
with no withholding.

**Special discount** (21)
A discount the law gives a person: senior citizens and PWDs pay no VAT on what qualifies, then 20% less.
Worked out once, in BuildingBlocks, so the order and the invoice always agree.

**Common table expression (CTE)** (20)
A named step inside a query, written with `WITH`. The aging report uses one.

**Sequence** (06)
A database counter that hands out each number once, even under load: the order numbers.

**Race condition** (08)
A bug that only shows when two things happen at the same moment, like two people buying the last item.

**`FOR UPDATE`** (08)
Locks the rows a query reads until the transaction ends. Anyone else who wants them waits.

**Lost update** (09)
Two people change the same thing; the second save silently overwrites the first.

**Optimistic concurrency** (09)
Don't lock; check on save that nobody changed the row since you read it. If someone did, refuse with 409.

**`xmin`** (09)
PostgreSQL's built-in row version, changed on every update. EF Core uses it as the concurrency token.

**Dapper** (07)
A small library that runs SQL and maps the rows to C# objects. Here it does every read.

**SQL function** (07)
A named query stored in the database, called like `SELECT * FROM sales.search_sales_orders(...)`. We use
functions because PostgreSQL procedures can't return rows.

**Least privilege** (22)
Give each account only the rights it needs. The app connects as `erp_app` (read and write rows); only
deployments use `erp_owner` (change tables).

**Default privileges** (22)
`ALTER DEFAULT PRIVILEGES`: rights given automatically on everything a role creates from now on.

## Events and the outbox

**Eventual consistency** (16)
A copy that catches up a moment later, through events. Fine for names; never for stock.

**Upsert** (16)
Insert if missing, update if present. Applying the same full record twice gives the same result.

**Inbox** (20)
The receiving side remembering what it already handled, like Sales' `applied_payment` table.

**Outbox** (11)
A table where each module writes its events in the same transaction as the change, so an event can't be
lost or sent for something that never happened.

**Interceptor** (11)
Code EF Core runs around `SaveChanges`. Ours copies domain events into the outbox.

**Dispatcher** (11)
A background service that reads the outbox and hands each event to its handlers.

**At-least-once delivery** (11)
Every event is delivered, possibly more than once.

**Idempotent** (11)
Doing it twice has the same effect as doing it once. Every event handler must be.

**`SKIP LOCKED`** (11)
Lets two dispatchers share the outbox: each skips rows the other has locked.

**`BackgroundService` / `IHostedService`** (11, 22)
Code ASP.NET Core starts with the app. A `BackgroundService` keeps running (the dispatcher); an
`IHostedService` can simply do one thing at startup (the first admin).

## Sign-in and security

**Authentication / authorization** (05)
Who are you? / What may you do?

**ASP.NET Core Identity** (05)
Microsoft's sign-in library: users, roles, password hashing, lockout, cookies, tokens. We write no sign-in
code ourselves.

**Password hash** (05)
A one-way, deliberately slow scramble of a password. The database never stores the password itself.

**Lockout** (05)
After 5 wrong passwords, the account refuses sign-in for 15 minutes.

**Claim** (05)
One fact about the signed-in user, such as their id or role.

**Bearer token / refresh token** (05)
The access token proves who you are for a short while; the refresh token buys a new one. For apps that
aren't browsers.

**Cookie** (05)
How a browser stays signed in. The browser sends it automatically.

**Security stamp** (05)
A random value stored with each user and copied into their tokens. Changing it ("sign out everywhere", a new
password) ends every session.

**`ICurrentUser`** (05)
The small interface handlers use to ask who's calling.

**Role** (03, 15)
`SalesRep`, `SalesManager`, `Admin`, and from lesson 15 `InventoryClerk`, `Purchaser`, `Accountant`. Each
person has exactly one.

**Rate limiting** (22)
A budget of requests per caller per minute. Over it, the API answers 429.

**HTTPS / HSTS** (22)
HTTPS encrypts the connection. HSTS tells the browser to never use plain HTTP for this site again.

**Security headers** (22)
Response headers that tell the browser to be careful: no framing, no guessing content types.

**CSP (Content Security Policy)** (22)
A header listing what a page may load. For our JSON responses: nothing at all. For the app's pages: only
files from our own site, and never an inline script.

**CORS** (22)
The browser rule that stops a web page on one site calling an API on another, unless the API allows it.

**API key** (05)
A secret per client *program*. We use none: a key shipped in a browser app is public. Every call is protected
by who's signed in.

**Data protection keys** (22)
The keys Identity uses to protect cookies and tokens. Stored in the database so restarts sign nobody out,
and encrypted with a certificate so a stolen backup can't forge a sign-in.

**Open redirect** (05)
A sign-in page that sends you anywhere after signing in, including an attacker's look-alike site. Ours only
follows paths inside the app.

## Observability

**Observability** (13)
Being able to tell what the running app is doing, from the outside.

**Logs, traces, metrics** (13)
Logs: what happened. Traces: where the time went in one request. Metrics: how much and how often, over time.

**Span** (13)
One timed step inside a trace: the HTTP request, the handler, one SQL query.

**Trace id** (13)
One id shared by every span and log line of a request. Returned as `X-Trace-Id`.

**OpenTelemetry / OTLP** (13)
The open standard for producing telemetry, and the protocol for sending it anywhere.

**Health checks** (13)
`/health/live`: is the process up? `/health/ready`: can it reach the database?

**Aspire dashboard** (13)
A development tool that shows logs, traces and metrics live. Not for production.

## Testing

**Unit test** (03)
Tests one piece, the domain, with no database. Fast.

**Integration test** (12)
Runs the whole API in memory against a real PostgreSQL database.

**`WebApplicationFactory`** (12)
Starts the API inside a test without a web server.

**Architecture test** (12)
A test that fails when a module breaks a boundary rule, such as referencing another module's main project.

**AwesomeAssertions** (03)
The readable `.Should().Be(...)` assertions. A free fork of FluentAssertions, whose newer versions are paid.

**Playwright** (12)
Drives a real browser from a test: open a page, type, click, wait for text.

## Deployment

**Container / image** (23)
An image is the app with everything it needs to run, frozen into one file. A container is a running copy of
it, isolated from the others.

**Docker Compose** (23)
Runs several containers together on one machine from one file, in the right order.

**Volume** (23)
Storage that outlives a container, so replacing the database container never touches the data.

**Caddy / reverse proxy** (23)
A web server in front of the app that takes care of HTTPS and passes requests on.

**Forwarded headers** (23)
`X-Forwarded-For` and `X-Forwarded-Proto`: how a proxy tells the app the real client address and scheme.

**Framework-dependent publish** (23)
A published app that uses the .NET runtime installed on the server, rather than carrying its own.

**`pg_dump` / `pg_restore`** (23)
PostgreSQL's tools to back up a database to one file, and to restore it.
