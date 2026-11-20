# Glossary — Every Term in Plain English

Keep this open while you read the lessons. Terms are grouped by topic, so related ideas sit together. Each
one has a plain meaning, the lesson where it first appears (in brackets), and sometimes an everyday picture
to hold on to. The glossary grows with the course: each lesson's new words arrive with the lesson.

---

## Your tools

**SDK** (01)
*Software development kit.* For .NET, the `dotnet` command plus the compiler and runtime: everything needed
to create, build, test and run an app.

**Package manager** (01)
A program that installs other programs with one command and keeps them up to date: **winget** on Windows,
**Homebrew** on macOS, **dnf** on Fedora, **apt** on Ubuntu.

**Terminal** (01)
The window where you type commands. This course uses Bash: **Git Bash** on Windows, Terminal on macOS and
Linux.

**PATH** (01)
The list of folders a terminal searches for programs. It's read when the terminal opens, which is why a
freshly installed tool needs a new terminal.

**Git** (01)
Keeps every saved version of your code. A **commit** is one saved version, with a message saying what
changed. A **repository** is a folder whose history Git keeps.

**NuGet package** (01)
A library someone published for .NET, downloaded by `dotnet` when a project asks for it.

**Central package management** (01)
Each project says *which* packages it uses; one file, `Directory.Packages.props`, says *which version*.

## The big architecture words

**Monolith** (01)
One program you deploy and run, as opposed to many separate services talking over a network.

**Modular monolith** (01)
One program, split inside into modules with real walls between them. *An island with one service window.*

**Module** (01)
One area of the business with its own code and its own data: Tickets, Sla, Knowledge, Identity. Everything
inside is `internal`.

**Microservices** (01)
Each area as its own program, talking over the network. Real walls, but every call between areas can fail
on the way.

**Vertical slice architecture** (01)
One folder per feature, holding everything that feature needs. *The taco drawer.*

**BuildingBlocks** (01)
The shared project every module may use: base classes and small interfaces.

**Infrastructure** (01)
The shared project holding the real implementations (database connection, current user). Only the host
references it.

**Host** (01)
The one program that runs: `Yasda.Api`. It plugs the modules in and serves the browser app.

**Dependency injection** (01)
Classes ask for what they need in their constructor, and the app hands it to them from its list of services
(`IServiceCollection`).

**Feature** (03)
One thing the product can do, in its own folder: a handler file and an endpoint file. *One drawer in the taco
drawer.*

**Handler** (03)
A plain class with one `HandleAsync` method that does a feature's work. It knows nothing about HTTP.

**Endpoint** (03)
The HTTP part of a feature: it turns a request into a message for the handler and the answer back into HTTP.

**Command / Query** (03)
The two kinds of message to a handler. A command changes something; a query only reads. Together:
**CQRS-lite**.

**Validator** (03)
A class (FluentValidation) that says what a well-formed request looks like. It runs before the endpoint, and a
bad request is answered `400 Bad Request`.

**Endpoint filter** (03)
Code ASP.NET Core runs before (and after) an endpoint. We set one on the whole `/api` group to run every
validator.

**Problem details** (03)
The standard JSON shape (RFC 9457) of an HTTP error: a title, a status, a detail and, for validation, the
messages grouped by field.

**Mediator** (03)
A library that sits between an endpoint and its handler. We don't use one: the endpoint calls its handler
directly.

**`internal`** (03)
A C# keyword: visible only inside the project. Everything in a module is `internal` except its entry class.

**Architecture test** (03)
A test that reads the compiled assemblies and fails when a structural rule breaks: an extra `public` class, a
forbidden reference, a folder named after a technical layer.

## The domain

**Domain** (04)
The business rules, as plain C# with no database and no web code. In Tickets: the `Ticket`, its messages, its
statuses and its error codes.

**Entity** (04)
Something with an identity that stays the same while its data changes. Ticket #7 is still #7 after it changes
hands.

**Value object** (04)
Defined only by its value, never changed. *Any ₱1,000 bill is as good as any other.* `TicketId(7)` equals any
other `TicketId(7)`.

**Aggregate / aggregate root** (04)
A group of objects changed as one unit, with one root the outside world talks to. A ticket and its messages.

**Typed id** (04)
A small struct (`TicketId`, `UserId`) used instead of a plain `long`, so the compiler refuses a mix-up.

**State machine** (04)
The allowed moves between statuses (New → Open → Resolved → Closed). Each action lists the statuses it allows.

**Domain exception** (04)
How a business rule says no: it carries an error code and becomes HTTP 422 (lesson 07).

**Error code** (04)
A stable name for one kind of refusal, such as `TICKETS-0007`, with a title and a fix. Never renumbered or
reused.

**Property-based test** (04)
A test that states a rule that must always hold and checks it against hundreds of generated inputs (FsCheck).

## The database

**PostgreSQL** (05)
The free, open-source database server this product stores everything in. Version 16 or newer.

**Schema** (05)
A named folder of tables inside one PostgreSQL database. Each module owns one, named after itself (`tickets`).

**EF Core** (05)
Entity Framework Core, Microsoft's library that saves and loads objects. Used for every write.

**DbContext** (05)
EF Core's main class: one instance is one conversation with the database, holding change tracking and a
`DbSet` per kind of thing.

**Change tracking** (05)
EF Core remembering every object it loaded, so on save it writes only what changed.

**Migration** (05)
A small file of C# that changes the schema (create this table, add that column), applied once and recorded in a
history table. Never edited after it has shipped.

**Check constraint** (05)
A rule the database itself enforces on a column, such as "status is one of these six names".

**Seed data** (05)
Sample rows added in development so there is something to click through. Idempotent: running it again adds
nothing.

**Idempotent** (05)
Doing it twice has the same result as doing it once.

**Connection string** (05)
The text that says which server, database and login to use. Configuration, never code.

**Collection** (05)
xUnit's way to say "these test classes share one fixture and run one after the other".

## Sign-in and security

**ASP.NET Core Identity** (06)
The library that ships with .NET for users, roles, password hashing, lockout and sign-in cookies. We never write
those ourselves.

**Authentication / authorization** (06)
Authentication asks "who are you?"; authorization asks "may you do this?". `UseAuthentication` and
`UseAuthorization`, in that order.

**Role** (06)
A name for what an account may do: `Admin`, `TeamLead`, `Agent`, `KbEditor`, `Requester`. Every account has exactly
one.

**Claim** (06)
One fact about the signed-in person (their id, name or role) carried in the cookie.

**Cookie** (06)
A small value the server asks the browser to keep and send back with every request. Ours is `HttpOnly` and
`SameSite=Strict`.

**Lockout** (06)
After five wrong passwords an account refuses all sign-ins for 15 minutes.

**Open redirect** (06)
A page that sends people wherever a link tells it to, including to another site. Prevented by only accepting
addresses inside the app.

**ICurrentUser** (06)
How a handler asks who is calling, without touching HTTP.

**Playwright** (06)
A library that drives a real browser for tests: it finds things by role and name and waits for what should appear.

**Browser context** (06)
One person's own browser session: their own cookies, like their own computer.

## Modules talking, and errors

**Contracts project** (07)
A module's service window: a small public project of interfaces and plain data that other modules may call. It
references only BuildingBlocks.

**Exception handler** (07)
The one class that turns whatever was thrown into a problem response: a `DomainException` becomes a 422 with its code.

**Catalog (error catalog)** (07)
The list of error codes a module can answer with, each with a title and a fix. The host serves all of them.

**OpenAPI** (07)
A standard JSON document that describes every address and answer of an API, generated from the endpoints.

**Status line** (07)
The place beside a page's title where what just happened, or what went wrong, is shown. An error carries its code.

**ApiClient** (07)
The browser app's one way of calling the API, so every page handles failures, busy state and an ended session alike.

**Extension method** (06)
A method you call on a type as if it belonged to it, written in a static class with `this` on the first parameter.

## Reading data

**Dapper** (08)
A tiny library that runs SQL and maps each row to a C# record. We use it for every read; EF Core for every write.

**SQL function** (08)
A named query stored in a PostgreSQL schema (`tickets.search_tickets(...)`), called with parameters. In PostgreSQL a
function can return rows; a "procedure" can't.

**SQL injection** (08)
Request text becoming part of a SQL statement, so the sender controls the database. Prevented by parameters and an allow-list.

**Allow-list** (08)
The fixed list of values a request may use (the sortable columns), checked before anything runs.

**CQRS-lite** (03, 08)
A command changes something; a query only reads. Here they even use different libraries.

**Query string** (08)
The `?status=Open&page=2` part of an address. A list's filters, sort and page live there.

**Embedded resource** (08)
A file compiled into an assembly, so a migration can read it from the module itself.

## Two people at once

**Race condition** (09)
A bug where the result depends on who gets there first. Two agents both see a ticket nobody has, and both take it.

**Row version** (09)
A value the database changes every time a row changes. A save that says "only if it's still the version I saw" can tell
that someone else got there first. In PostgreSQL it is the hidden `xmin` column.

**Optimistic concurrency** (09)
Letting everyone read freely, and catching the conflict when they save. The loser gets an exception and a `409`.

**Shadow property** (09)
A column EF Core keeps track of without a field in the C# class, so the domain stays free of database details.

**Conflict (409)** (09)
The answer to "someone else changed this first". Show the message, then show the thing as it is now.

**Internal note** (11)
A message on a ticket that only staff can read. It is stored in the same conversation as a reply, with `IsInternal` set, and
the query leaves it out for a requester.

**Child row** (11)
A row that belongs to another, such as a ticket's message. Saving a new one doesn't touch the parent's row, so the parent's row
version isn't checked unless something else about the parent changes too.

## The browser app

**Blazor Web App** (01)
Microsoft's shape for a .NET web app: the server sends the page, and the screens inside it run where you
choose.

**WebAssembly** (01)
A way for browsers to run compiled code. Blazor uses it to run .NET, and our C#, inside the browser.

**Render mode** (01)
Where a screen runs. Ours is interactive WebAssembly: in the browser.

**Prerendering** (01)
The server drawing a screen first, before the browser takes over. Switched off here, because our screens are
API data the server would fetch twice.

**Router** (01)
Reads the address and shows the page whose `@page` line matches.

**Layout** (01)
The frame around every page. The WebForms master page.

**Landmark** (01)
An HTML element that says what part of the page it is (`<header>`, `<nav>`, `<main>`), so screen readers and
tests can jump straight to it.

**Custom property** (01)
A named value in CSS (`--link: #0069C2`), used elsewhere as `var(--link)`. Our whole look is a handful of
them.

**SVG** (01)
A picture written as text. Sharp at any size, tiny to download. Our logo.

**Static assets** (01)
The files the server sends unchanged: the app itself, `app.css`, the logo. `MapStaticAssets` serves them,
compressed and cached.

**Fingerprint** (01)
A short code in a file's name (`app.tl0z91yx0a.css`) that changes whenever the file does, so a browser never
uses an old copy.

**Inline confirmation** (10)
Asking "are you sure?" on the page itself, with a sentence naming the consequence and buttons that say what they do
("Cancel ticket" / "Not yet"), instead of a pop-up dialog.

## Testing

**Test** (02)
A check written down once and run forever.

**Arrange, Act, Assert** (02)
The three parts of a test: get ready, do the one thing, check what happened.

**Unit test** (02)
Tests one rule or class alone. No server, no database. *One bolt, on the bench.*

**Integration test** (02)
Tests the real API, started in memory, with real requests. *The engine, on a stand.*

**End-to-end test** (02)
A real browser clicking through the real app. *A test drive.*

**Test pyramid** (02)
Many fast unit tests, fewer integration tests, a few end-to-end tests.

**Fact / Theory** (02)
In xUnit, a `[Fact]` is one test; a `[Theory]` is one test run with several sets of inputs (`[InlineData]`).

**Fixture** (02)
Something expensive that several tests share, created once: for us, the started app.

**WebApplicationFactory** (02)
Starts our real app inside the test process, with no port, and hands out clients wired straight to it.

**Microsoft.Testing.Platform** (02)
The test runner .NET 10 uses; `global.json` tells `dotnet test` to use it.

**Edge case** (02)
An input at the border of what's allowed: empty, at the limit, twice, at the same instant.

**Regression test** (02)
A test run again after every change; in the narrow sense, one written after a bug so the bug can't come
back.

**Smoke test** (02)
A few quick checks that the app starts and its front page loads.

**Coverage** (12)
The share of your code's lines that ran while the tests ran. It shows what no test touches, not whether the tests that ran
check anything.

**Mutation testing, mutant, killed, survived** (12)
A tool changes your code in small ways, one at a time (`>` to `>=`, a line removed), and runs the tests on each changed copy,
a **mutant**. A mutant a test catches is **killed**; one every test passes is **survived**, a bug your tests would miss. The
share killed is the **mutation score**.

**Continuous integration (CI)** (12)
A server that builds and tests every change as soon as it's pushed. *Your machine says "it works for me"; CI says "it works
for anyone".*

---

## Containers

**Container image** (12)
A program plus everything it needs to run, frozen into one file. `postgres:18` is PostgreSQL 18, ready to start. *A shipping
container packed at the factory.*

**Container** (12)
A running copy of an image, isolated from the rest of the machine. Start it, use it, throw it away.

**Registry** (12)
A library of images. `docker.io/library/postgres:18` is the Docker Hub registry's official `postgres` image, version 18.

**Volume** (12)
Storage that lives outside a container, so the data survives when the container is removed.

**Podman** (12)
Runs containers: free (Apache-2.0), rootless, no background service. It runs the same OCI images as Docker.

**Rootless** (12)
A container started with only your own user's rights, so something that escapes it can do no more than you could.

**OCI** (12)
The standard that image formats follow. Because of it, an image built with Podman runs on Docker, and the other way round.

**Testcontainers** (12)
A library that starts a container from inside a test, waits until it's ready, hands over its address and removes it when the
tests end.

**Ryuk** (12)
Testcontainers' cleaner container: it removes the test containers even if the test run crashes.

**GitHub Actions, workflow** (12)
GitHub's servers running a list of steps (a **workflow**, a YAML file in `.github/workflows`) whenever something happens in the
repository, such as a push.

---

## Events and the outbox

**Domain event** (13)
A fact a module's domain records in its own words ("ticket created"). Saved in the same transaction as the change, `internal` to its module.

**Integration event** (13)
The public form of an event: plain data in a `.Contracts` project, which other modules listen to.

**Transactional outbox** (13)
A table in each module where events are saved in the same transaction as the change that raised them. *The mailbox: the aggregate writes the letter.*

**Dispatcher** (13)
The background service that reads the outbox after the commit and delivers each event to its handlers. *The post office.*

**At least once** (13)
The guarantee that an event is delivered one or more times, never zero. The price: a handler may see it twice.

**Idempotent** (13)
Safe to run twice: the second run leaves the same result as the first.

**Parked message** (13)
An outbox message given up on after ten failed deliveries, left in the table with its error for a person to look at.

**Dual write** (13)
Changing two things (a database and another module) with no transaction around both, so a crash can leave one done and not the other.

**Sequence** (13)
PostgreSQL's own counter. `nextval` takes the next number, so a ticket's number is known before it is saved.

**Partial index** (13)
An index of only the rows that match a condition, such as the outbox rows still waiting.

**SLA, SLA clock, SLA policy** (13)
A service level agreement: how long a team has to answer and to resolve a ticket. The **clock** holds one ticket's two deadlines; the **policy** is the row that says how long each priority gets.

**FakeTimeProvider** (13)
A clock a test controls: set it, move it, by hand.

**Eventual consistency** (14)
A copy that catches up a moment after the original changes, like Tickets' copy of the deadlines.

**Event version** (14)
A counter on an event so a receiver can ignore an older answer that arrives after a newer one.

---

## Observability

**Log, metric, trace** (15)
A **log** is a line about one thing that happened. A **metric** is a number over time. A **trace** is one request's path as a tree of timed steps.

**Span** (15)
One timed step inside a trace: the request, a database command, a delivery.

**OpenTelemetry (OTLP)** (15)
The open standard for producing traces, metrics and logs, and its protocol (OTLP). The app sends to one endpoint; any tool can listen.

**Counter, gauge** (15)
A **counter** only goes up (messages delivered). A **gauge** goes up and down and is read when metrics are collected (messages waiting).

**traceparent** (15)
The W3C text that carries a trace id and span id. Saved in the outbox row, it lets a delivery join the trace of the request that caused it.

**Liveness, readiness, degraded** (15)
**Liveness**: is the process running? **Readiness**: can it do its job now? **Degraded**: working, but something needs a person.

**Aspire dashboard** (15)
A development-only page that shows traces, logs and metrics as you use the app. MIT-licensed, never deployed.

