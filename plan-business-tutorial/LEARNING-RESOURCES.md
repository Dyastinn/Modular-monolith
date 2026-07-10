# Learning Resources — Where to Go Deeper on Each Concept

> 👨‍🏫 **From your senior:** You don't need to study all of this. Use it like a doctor's referral: when
> a specific concept in a phase won't click, come here, find that concept, and go do a *little* focused
> practice. Then come back. Learning in small, targeted doses beats trying to read a whole textbook.

> ⚠️ **About links:** I've named official, stable sources rather than pasting fragile deep links.
> The reliable move is to **search the exact phrase I give you** on the site named. Official docs get
> reorganized; the search box doesn't.

---

## The absolute basics (do these first if you're new to C#)

| If you're shaky on... | Go learn... | Where |
|---|---|---|
| C# language itself | "C# tutorial for beginners" — variables, classes, methods | **Microsoft Learn** (learn.microsoft.com) → search *"C# for beginners"*. There's a free video series with that exact name. |
| Just want to *run* C# in a browser | Interactive C# lessons, no setup | Microsoft Learn → *"Learn C# with online tutorials"* (runs in the browser). |
| What .NET even is | ".NET fundamentals overview" | Microsoft Learn → search *".NET fundamentals"*. |

**A concrete first-week plan if you're brand new:**
1. Do a "C# for beginners" video series (a weekend).
2. Write a tiny console app that makes a `Dog` class with a `Bark()` method and calls it. That's it.
3. *Then* start Phase 0. You'll be ready.

---

## Concept-by-concept referrals

### Interfaces & Dependency Injection (needed from [Phase 1](01-solution-setup.md) on)
- Search Microsoft Learn for **"Dependency injection in .NET"** — the official guide. Read just the
  intro and the first example.
- Search for **"C# interfaces"** on Microsoft Learn.
- 🎯 **Practice:** make an `IAnimal` interface with a `Speak()` method, then a `Dog` and a `Cat` that
  implement it differently. Feel how the caller can hold an `IAnimal` without knowing which one.

### async / await (needed from [Phase 2](02-mediator-own-vs-free.md) on)
- Search Microsoft Learn for **"Asynchronous programming with async and await"**.
- 🎯 Don't go deep. You only need "await means wait for the answer." Come back to the theory later.

### ASP.NET Core, Minimal APIs & Carter (the web endpoints — [Phase 1](01-solution-setup.md) & [2](02-mediator-own-vs-free.md))
- Search Microsoft Learn for **"Minimal APIs overview"** and **"Tutorial: Create a minimal API"**.
- Carter: search **"Carter GitHub"** — the README shows the `ICarterModule` pattern we use so each feature
  owns its own endpoint. It's a thin layer *over* minimal APIs, so learn minimal APIs first.
- 🎯 **Practice:** build a hello-world minimal API with one `GET /hello` that returns `"hi"`, then move that
  route into an `ICarterModule` and call `app.MapCarter()`. That's literally our `/sales/ping` skeleton.

### Dapper & stored procedures ([Phase 3](03-database-schema.md), [3b](03b-data-access.md), [6](06-create-order.md))
- Dapper's own docs: search for **"Dapper GitHub"** (the project README is the best tutorial).
- Stored procedures: Microsoft Learn → **"CREATE PROCEDURE (Transact-SQL)"** and
  **"Stored procedures (Database Engine)"**.
- 🎯 **Practice:** in SQL Server, create a tiny `Person` table and a `usp_Person_Insert` proc, then
  call it from a 20-line C# console app using Dapper. Once that works, Phase 6's repository is 80% familiar.

### Password hashing & JWTs ([Phase 4](04-jwt-auth.md), all five parts)
- Microsoft Learn: search **"Rfc2898DeriveBytes.Pbkdf2"** and **"ASP.NET Core JWT bearer authentication"**.
- For the token format itself, search **"JWT introduction"** — any explainer that shows the three
  dot-separated parts and stresses that the payload is *signed, not encrypted*.
- 🎯 **Practice:** paste a token from your own `POST /auth/login` into a JWT decoder website and read the
  claims. Seeing your own `role: SalesRep` in plain text is the moment "signed, not encrypted" lands.

### Records, structs, enums ([Phase 5](05-domain-model.md))
- Microsoft Learn: search **"C# records"**, **"C# struct types"**, **"C# enumeration types"**.
- 🎯 **Practice:** make `readonly record struct Money(decimal Amount)` and try to create a
  `Money(-5)` — then add the check that throws. That *is* Phase 5's value-object idea in miniature.

### FluentValidation (input validation — [Phase 6](06-create-order.md), wired up in [Phase 11](11-cross-cutting.md))
- Search **"FluentValidation documentation"** — read just the "Creating your first validator" page.
- 🎯 **Practice:** write an `AbstractValidator<T>` with two `RuleFor` lines. Notice it only checks *shape*
  (is the field present/positive?), never *business rules* — that distinction is the whole Phase 6 lesson.

### Transactions & concurrency ([Phase 7](07-submit-reserve-concurrency.md) — the hard one)
- Microsoft Learn: search **"SQL Server transactions"** and **"Transaction locking and row versioning
  guide"**.
- Concept to internalize: **race condition**. Search *"race condition explained"* — any beginner
  explanation with the "two people withdraw from one bank account at once" example.
- 🎯 This is genuinely the hardest idea in the whole project. Re-read [Phase 7 §5](07-submit-reserve-concurrency.md)
  — the naive C# version, then the timeline — three times. It's supposed to be hard. Don't feel bad.

### Unit testing with xUnit (starts in [Phase 5 §10](05-domain-model.md), completed in [Phase 12](12-testing.md))
- Microsoft Learn: search **"Unit testing C# with xUnit"** (there's a tutorial by that name).
- 🎯 **Practice:** write one test that asserts `2 + 2 == 4` using xUnit and run it. Silly, but it
  teaches you the `[Fact]` + `Assert` rhythm you'll use for the real domain tests.

---

## The big architecture ideas (read *after* you've felt them in the phases)

> 👨‍🏫 Honestly? These concepts make *far* more sense after you've built a bit than before. Don't
> front-load them. Build Phases 1–4 first, *then* if you want the "grand theory," read these.

| Concept | What to search for | Note |
|---|---|---|
| Modular Monolith | Search *"Modular Monolith Milan Jovanović"* and *"Modular Monolith with DDD Kamil Grzybek"* | The architecture this whole project uses. Grzybek's sample repo is the canonical deep example. |
| Vertical Slice Architecture | Search *"Vertical Slice Architecture Jimmy Bogard"* | Jimmy Bogard coined the popular version; his talks are approachable. This is how we organize *inside* each module. |
| Domain-Driven Design (DDD) | *"Domain-Driven Design distilled"* (a famously short, readable book by Vaughn Vernon) | The short one. Avoid the giant "blue book" until much later. |
| Clean Architecture (for contrast) | *"Clean Architecture Uncle Bob"* — his original blog post is free and short | We deliberately do **not** use its four-layer split — read it to understand *what we replaced and why* (see [Phase 1 §2](01-solution-setup.md)). |
| CQRS | Microsoft Learn → *"CQRS pattern"* in the Azure Architecture Center | Read it, then re-read [Phase 2 §2](02-mediator-own-vs-free.md) and [Phase 10](10-queries-search.md) to see why we do the *lite* version. |
| Mediator pattern | Search *"mediator pattern refactoring guru"* | refactoring.guru has the friendliest diagrams for design patterns. |
| MediatR (the famous library) | Search *"MediatR GitHub"* | Skim it to see our `Sender.cs` is the same shapes. Note we don't *use* it — it moved to a paid license for larger orgs in 2025, which is why [Phase 2](02-mediator-own-vs-free.md) compares the MIT-licensed `martinothamar/Mediator` instead. |

---

## How to actually practice (not just read)

Reading about code is like reading about swimming. You have to get in the water.

1. **Type the examples yourself.** Don't copy-paste. Typing forces you to read every line. You'll make
   typos, the compiler will complain, and *fixing those complaints is where learning happens.*
2. **Break things on purpose.** Delete a line, see what error you get, put it back. Now you know what
   that error *means* next time.
3. **Explain it to a rubber duck.** Seriously — say the concept out loud to an object on your desk. If
   you get stuck mid-sentence, that's the exact spot you don't understand yet. (This is a real,
   named technique: "rubber duck debugging.")
4. **Keep a `what-i-learned.md` file.** One line per "aha." You'll be amazed looking back in a month.

---

## When you're stuck for more than ~20 minutes

Being stuck is part of the job — but *staying* stuck alone isn't productive. In order:

1. Re-read the relevant phase section slowly, out loud.
2. Check the [GLOSSARY.md](GLOSSARY.md) and [00a-FOUNDATIONS.md](00a-FOUNDATIONS.md) for any word that's
   fuzzy.
3. Try the phase's **Check Yourself** questions — sometimes attempting the answer explains the question.
4. Search the *exact error message* (copy-paste it into a search engine). Someone has hit it before.
5. Ask a specific question. "It doesn't work" is unanswerable; "I expected X, I got Y, here's my code"
   gets help fast.

You're allowed to be slow. Slow and understanding beats fast and confused every time.
