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

### Interfaces & Dependency Injection (needed from Phase 1 on)
- Search Microsoft Learn for **"Dependency injection in .NET"** — the official guide. Read just the
  intro and the first example.
- Search for **"C# interfaces"** on Microsoft Learn.
- 🎯 **Practice:** make an `IAnimal` interface with a `Speak()` method, then a `Dog` and a `Cat` that
  implement it differently. Feel how the caller can hold an `IAnimal` without knowing which one.

### async / await (needed from Phase 2 on)
- Search Microsoft Learn for **"Asynchronous programming with async and await"**.
- 🎯 Don't go deep. You only need "await means wait for the answer." Come back to the theory later.

### Records, structs, enums (Phase 3)
- Microsoft Learn: search **"C# records"**, **"C# struct types"**, **"C# enumeration types"**.
- 🎯 **Practice:** make `readonly record struct Money(decimal Amount)` and try to create a
  `Money(-5)` — then add the check that throws. That *is* Phase 3's value-object idea in miniature.

### ASP.NET Core, Minimal APIs & Carter (the web endpoints, Phase 1 & 4)
- Search Microsoft Learn for **"Minimal APIs overview"** and **"Tutorial: Create a minimal API"**.
- Carter: search **"Carter GitHub"** — the README shows the `ICarterModule` pattern we use so each feature
  owns its own endpoint. It's a thin layer *over* minimal APIs, so learn minimal APIs first.
- 🎯 **Practice:** build a hello-world minimal API with one `GET /hello` that returns `"hi"`, then move that
  route into an `ICarterModule` and call `app.MapCarter()`. That's literally our `/sales/ping` skeleton.

### FluentValidation (input validation, Phase 4)
- Search **"FluentValidation documentation"** — read just the "Creating your first validator" page.
- 🎯 **Practice:** write an `AbstractValidator<T>` with two `RuleFor` lines. Notice it only checks *shape*
  (is the field present/positive?), never *business rules* — that distinction is the whole Phase 4 lesson.

### Dapper & stored procedures (Phases 4–6)
- Dapper's own docs: search for **"Dapper GitHub"** (the project README is the best tutorial).
- Stored procedures: Microsoft Learn → **"CREATE PROCEDURE (Transact-SQL)"** and
  **"Stored procedures (Database Engine)"**.
- 🎯 **Practice:** in SQL Server, create a tiny `Person` table and a `usp_Person_Insert` proc, then
  call it from a 20-line C# console app using Dapper. Once that works, Phase 4 is 80% familiar.

### Transactions & concurrency (Phase 5 — the hard one)
- Microsoft Learn: search **"SQL Server transactions"** and **"Transaction locking and row versioning
  guide"**.
- Concept to internalize: **race condition**. Search *"race condition explained"* — any beginner
  explanation with the "two people withdraw from one bank account at once" example.
- 🎯 This is genuinely the hardest concept in the whole project. Re-read Phase 5 Step 3 three times.
  It's supposed to be hard. Don't feel bad.

### Unit testing with xUnit (Phase 8)
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
| Clean Architecture (for contrast) | *"Clean Architecture Uncle Bob"* — his original blog post is free and short | We deliberately do **not** use its four-layer split — read it to understand *what we replaced and why* (see Phase 0 §3). |
| CQRS | Microsoft Learn → *"CQRS pattern"* in the Azure Architecture Center | Read it, then re-read our Phase 6 to see why we do the *lite* version. |
| Mediator pattern | Search *"mediator pattern refactoring guru"* | refactoring.guru has the friendliest diagrams for design patterns. |
| MediatR (the real library) | Search *"MediatR GitHub"* | We hand-rolled our own; skim theirs to see it's the same shapes. |

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
