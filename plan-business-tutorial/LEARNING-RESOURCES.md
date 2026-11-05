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

**Minimal APIs and Carter** (from [lesson 02](02-mediator.md))
Search Microsoft Learn for *"Minimal APIs overview"*. For Carter, search *"Carter GitHub"*: the README shows
the `ICarterModule` pattern. Learn minimal APIs first; Carter is a thin layer over them.

**`async` and `await`** (from [lesson 02](02-mediator.md))
Search *"Asynchronous programming with async and await"*. Don't go deep: "await means wait for the answer"
is all the course needs.

**The mediator and FluentValidation** ([lesson 02](02-mediator.md))
Search *"mediator pattern refactoring guru"* for friendly diagrams, and *"martinothamar Mediator GitHub"* for
the library we use. For validation, read only the *"Creating your first validator"* page of the
FluentValidation documentation. Notice that a validator checks shape, never business rules.

**Records, structs and enums** ([lesson 03](03-domain-model.md))
Search Microsoft Learn for *"C# records"*, *"C# struct types"* and *"C# enumeration types"*. To practice,
write `readonly record struct Money(decimal Amount)`, try `new Money(-5)`, then add the check that throws.
That's lesson 03's value object in miniature.

**Unit testing with xUnit** ([lesson 03](03-domain-model.md))
Search *"Unit testing C# with xUnit"*. Write one test that asserts `2 + 2 == 4`, and run it. Silly, but it
teaches the rhythm.

**PostgreSQL and SQL** ([lesson 04](04-database.md))
The official tutorial at postgresql.org, *"Tutorial"* in the documentation, covers tables, queries and joins
in an afternoon. For schemas, search *"PostgreSQL schemas documentation"*.

**EF Core** ([lesson 04](04-database.md) onwards)
Search Microsoft Learn for *"Getting started with EF Core"*, then *"EF Core migrations overview"*. For the
provider, search *"Npgsql Entity Framework Core provider"*. Read about change tracking last; it makes sense
once you've saved a few objects.

**ASP.NET Core Identity** ([lesson 05](05-sign-in.md))
Search Microsoft Learn for *"Use Identity to secure a Web API backend for SPAs"*: it's the page about
`MapIdentityApi`. For why we use length over composition rules, search *"NIST SP 800-63B passwords"*.

**Transactions and concurrency** ([lesson 07](07-submit.md) and [lesson 08](08-approve-reject-cancel.md))
Search *"race condition explained"* for the classic "two people withdraw from one account" example, then the
PostgreSQL documentation's *"Explicit locking"* and *"Transaction isolation"* pages. For EF Core's side,
search *"EF Core handling concurrency conflicts"*. This is the hardest idea in the course. Re-read lesson 07's
naive version and its timeline more than once; it's supposed to be hard.

**Dapper and SQL functions** ([lesson 10](10-reads.md))
Search *"Dapper GitHub"*: the README is the best tutorial. For functions, the PostgreSQL documentation's
*"CREATE FUNCTION"* page; look at the `RETURNS TABLE` examples.

**The outbox** ([lesson 11](11-events-outbox.md))
Search *"transactional outbox pattern microservices.io"*. It's written for microservices, but the problem it
solves, saving and announcing in one step, is exactly ours.

**OpenTelemetry** ([lesson 12](12-observability.md))
Search *".NET observability with OpenTelemetry"* on Microsoft Learn, and read *"Observability primer"* on
opentelemetry.io for what logs, traces and metrics each answer.

**Web security** ([lesson 13](13-security-hardening.md))
The OWASP *"Cheat Sheet Series"* has a short page for each topic: *"HTTP Strict Transport Security"*,
*"Content Security Policy"*, *"Authentication"*. For ASP.NET Core's limiter, search *"Rate limiting middleware
in ASP.NET Core"*.

**Integration tests** ([lesson 14](14-testing.md))
Search Microsoft Learn for *"Integration tests in ASP.NET Core"*: it explains `WebApplicationFactory`.

**IIS and backups** ([lesson 15](15-deploy-iis.md), [lesson 16](16-production-checklist.md))
Search Microsoft Learn for *"Host ASP.NET Core on Windows with IIS"* and *"Troubleshoot ASP.NET Core on
Azure App Service and IIS"*: the second has every 500.xx code. For backups, the PostgreSQL documentation's
*"Backup and Restore"* chapter.

## The big architecture ideas

These make far more sense after you've built a few lessons than before. Don't front-load them.

| Idea | Search for | Why |
|---|---|---|
| Modular monolith | *"Modular Monolith with DDD Kamil Grzybek"* | The deepest public example of the architecture this course uses |
| Vertical slices | *"Vertical Slice Architecture Jimmy Bogard"* | How we organize inside each module |
| Domain-driven design | *"Domain-Driven Design Distilled"*, a short book by Vaughn Vernon | The short one. Leave the big "blue book" for much later |
| Clean Architecture, for contrast | *"The Clean Architecture"*, Robert C. Martin's original blog post | We don't use its layer split. Read it to understand what we replaced, and why |
| CQRS | *"CQRS pattern"* in the Azure Architecture Center | Then re-read lesson 02 and lesson 10 to see why we do the lite version |

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
