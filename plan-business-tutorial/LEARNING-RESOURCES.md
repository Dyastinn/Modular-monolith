# Learning Resources — Where to Go Deeper

Good day, developer! You don't need to study all of this. Use it like a doctor's referral: when one idea in a
lesson won't click, find it here, do a little focused practice, and come back. Small, targeted doses beat
reading a whole textbook.

About the links: this page names official, stable sources and gives you the exact phrase to search for,
instead of deep links that break when a site is reorganized. The search box doesn't break.

## The absolute basics

| If you're shaky on | Search for | Where |
|---|---|---|
| The C# language | *"C# for beginners"*: a free video series | Microsoft Learn (learn.microsoft.com) |
| Running C# with no setup | *"Learn C# with online tutorials"*: runs in the browser | Microsoft Learn |
| What .NET is | *".NET fundamentals"* | Microsoft Learn |

If you're brand new, spend one weekend on the beginners' series, write a tiny console app with a `Dog` class
and a `Bark()` method, and then start [lesson 01](01-solution-setup.md). Read
[Foundations](00a-FOUNDATIONS.md) first, too.

## Lesson by lesson

**Interfaces and dependency injection** (from [lesson 01](01-solution-setup.md))
Search Microsoft Learn for *"Dependency injection in .NET"*; the introduction and first example are enough.
To practice, make an `IAnimal` with a `Speak()` method, and a `Dog` and a `Cat` that answer differently.
Notice that the caller holds an `IAnimal` without knowing which one it got.

**Minimal APIs** (from [lesson 03](03-features.md))
Search Microsoft Learn for *"Minimal APIs overview"*, then *"Route handlers in Minimal API apps"*, the part
about `MapGroup`: each of our modules maps its endpoints that way.

**`async` and `await`** (from [lesson 03](03-features.md))
Search *"Asynchronous programming with async and await"*. Don't go deep: "await means wait for the answer"
is all the course needs.

**Handlers, endpoint filters and FluentValidation** ([lesson 03](03-features.md))
Search Microsoft Learn for *"Filters in Minimal API apps"* (our validation filter is one) and *"Create
responses in Minimal API applications"* (why `TypedResults`). For validation, read only the *"Creating your
first validator"* page of the FluentValidation documentation. Notice that a validator checks shape, never
business rules. Curious about the mediator pattern we chose not to use? Search *"mediator pattern refactoring
guru"*, then read lesson 03's "What about a mediator?" again.

**Blazor and MudBlazor** (from [lesson 01](01-solution-setup.md))
Search Microsoft Learn for *"ASP.NET Core Blazor"* and read *"Razor component"* first, then *"ASP.NET Core
Blazor render modes"*: we use a Blazor Web App with interactive WebAssembly everywhere. For components, the MudBlazor site's own docs show every component with
live examples you can copy.

**Records, structs and enums** ([lesson 04](04-domain-model.md))
Search Microsoft Learn for *"C# records"*, *"C# struct types"* and *"C# enumeration types"*. To practice,
write `readonly record struct Money(decimal Amount)`, try `new Money(-5)`, then add the check that throws.
That's lesson 04's value object in miniature.

**Testing, and xUnit** ([lesson 02](02-testing.md) onwards)
Search *"Unit testing C# with xUnit"* on Microsoft Learn, and *"Integration tests in ASP.NET Core"*, which
explains `WebApplicationFactory`. For the habit of hunting edges, search *"boundary value analysis"*: an old
idea, and still the one that finds the most bugs per minute.

**PostgreSQL and SQL** ([lesson 05](05-database.md))
The official tutorial at postgresql.org, *"Tutorial"* in the documentation, covers tables, queries and joins
in an afternoon. For schemas, search *"PostgreSQL schemas documentation"*.

**EF Core** ([lesson 05](05-database.md) onwards)
Search Microsoft Learn for *"Getting started with EF Core"*, then *"EF Core migrations overview"*. For the
provider, search *"Npgsql Entity Framework Core provider"*. Read about change tracking last; it makes sense
once you've saved a few objects.

**ASP.NET Core Identity** ([lesson 06](06-sign-in.md))
Search Microsoft Learn for *"Use Identity to secure a Web API backend for SPAs"*: it's the page about
`MapIdentityApi`. For why we use length over composition rules, search *"NIST SP 800-63B passwords"*.

**Transactions and concurrency** ([lesson 09](09-submit.md) and [lesson 10](10-approve-reject-cancel.md))
Search *"race condition explained"* for the classic "two people withdraw from one account" example, then the
PostgreSQL documentation's *"Explicit locking"* and *"Transaction isolation"* pages. For EF Core's side,
search *"EF Core handling concurrency conflicts"*. This is the hardest idea in the course. Re-read lesson 09's
naive version and its timeline more than once; it's supposed to be hard.

**Dapper and SQL functions** ([lesson 08](08-find-orders.md))
Search *"Dapper GitHub"*: the README is the best tutorial. For functions, the PostgreSQL documentation's
*"CREATE FUNCTION"* page; look at the `RETURNS TABLE` examples.

**The outbox** ([lesson 13](13-events-outbox.md))
Search *"transactional outbox pattern microservices.io"*. It's written for microservices, but the problem it
solves, saving and announcing in one step, is exactly ours.

**OpenTelemetry** ([lesson 14](14-observability.md))
Search *".NET observability with OpenTelemetry"* on Microsoft Learn, and read *"Observability primer"* on
opentelemetry.io for what logs, traces and metrics each answer.

**Web security** ([lesson 23](23-security-hardening.md))
The OWASP *"Cheat Sheet Series"* has a short page for each topic: *"HTTP Strict Transport Security"*,
*"Content Security Policy"*, *"Authentication"*. For ASP.NET Core's limiter, search *"Rate limiting middleware
in ASP.NET Core"*.

**Browser tests** ([lesson 06](06-sign-in.md))
The Playwright for .NET docs at playwright.dev, *"Writing tests"* and *"Locators"*.

**Property-based testing** ([lesson 04](04-domain-model.md))
FsCheck's docs at fscheck.github.io, *"QuickStart"* and *"Test data: generators, shrinkers and Arbitrary
instances"*.

**Coverage and mutation testing** ([lesson 11](11-update-order.md))
Stryker.NET's docs at stryker-mutator.io, *"Getting started"* and *"Configuration"*; ReportGenerator's README on
GitHub for the report types.

**Containers, Podman and CI** ([lesson 12](12-containers-ci.md))
podman.io's *"Getting started"*, and Podman Desktop's docs at podman-desktop.io for Windows and macOS. For
Testcontainers, dotnet.testcontainers.org, *"Modules: PostgreSQL"*. For CI, GitHub's docs: *"Building and
testing .NET"* under GitHub Actions.

**Integration events and idempotency** ([lesson 17](17-item-catalog.md), [lesson 21](21-payments.md))
Search *"idempotent consumer pattern microservices.io"*. Like the outbox page, it's written for microservices
and fits our modules exactly.

**Philippine invoicing rules** ([lesson 20](20-invoicing.md))
The BIR's [RMC 77-2024 digest](https://bir-cdn.bir.gov.ph/BIR/pdf/RMC%20No.%2077-2024%20Digest.pdf) answers
the common questions in a few pages. For the list itself, Section 6 of
[RR 7-2024](https://www.offshore-management.com.ph/blog/wordpress/wp-content/uploads/2024/05/RR-7-2024_Full.pdf)
(a copy; the official one is on bir.gov.ph). A plain-language walk-through:
[How to comply with the BIR's invoicing requirements](https://mtfcounsel.com/2025/05/08/how-to-comply-with-the-birs-invoicing-requirements/).
None of these replaces a tax adviser, who should see the printed invoice before the first real one.

**Images, Compose and rootless servers** ([lesson 24](24-deploy-podman.md))
For the `Dockerfile`, the *"Multi-stage builds"* page in Docker's docs (the format is the same for Podman); the
*"Compose file reference"* at compose-spec.io; and podman.io's *"Rootless tutorial"*. For HTTPS,
Caddy's docs, *"Automatic HTTPS"*. For backups, the PostgreSQL docs' *"Backup and Restore"* chapter.

**Backups and the restore drill** ([lesson 25](25-production-checklist.md))
The PostgreSQL documentation's *"Backup and Restore"* chapter, especially `pg_dump` and `pg_restore`.

## The big architecture ideas

These make far more sense after you've built a few lessons than before. Don't front-load them.

| Idea | Search for | Why |
|---|---|---|
| Modular monolith | *"Modular Monolith with DDD Kamil Grzybek"* | The deepest public example of the architecture this course uses |
| Vertical slices | *"Vertical Slice Architecture Jimmy Bogard"* | How we organize inside each module |
| Domain-driven design | *"Domain-Driven Design Distilled"*, a short book by Vaughn Vernon | The short one. Leave the big "blue book" for much later |
| Clean Architecture, for contrast | *"The Clean Architecture"*, Robert C. Martin's original blog post | We don't use its layer split. Read it to understand what we replaced, and why |
| CQRS | *"CQRS pattern"* in the Azure Architecture Center | Then re-read lesson 03 and lesson 08 to see why we do the lite version |

## How to actually practice

Reading about code is like reading about swimming. You have to get in the water.

1. **Type the examples yourself.** Typing makes you read every line, and fixing your own typos is where the
   learning happens.
2. **Break things on purpose.** Delete a line, read the error, put it back. Now you know that error.
3. **Explain it out loud**, to a colleague or to a rubber duck on your desk. Where you stumble is exactly
   what you don't understand yet.
4. **Keep a `what-i-learned.md` file.** One line per "aha". Look back in a month.

## When you've been stuck for twenty minutes

1. Re-read the section slowly, out loud.
2. Look up any fuzzy word in the [glossary](GLOSSARY.md) or [Foundations](00a-FOUNDATIONS.md).
3. Try the lesson's "Let's check!" questions. Sometimes answering one explains another.
4. Search the exact error message. Someone has hit it before.
5. Ask a specific question. "It doesn't work" can't be answered; "I expected X, I got Y, here's my code" gets
   help fast.

You're allowed to be slow. Slow and understanding beats fast and confused every time. Good luck, developer!

---

← [Start here](00-START-HERE.md)
