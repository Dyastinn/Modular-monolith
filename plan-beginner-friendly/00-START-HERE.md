# 00 — Start Here (Read This First)

Welcome. This folder's job is to make you **understand the concepts really well** — *what problem* each
piece solves and *how* it works — in plain English, with everyday analogies and lots of "here's *why*." You
build a small teaching slice as you go, but the goal here is **understanding**, not shipping the finished
product. Once the ideas click, the companion folders are where you *do* it.

> **Three folders, same architecture, three different jobs:**
> - **This folder (`plan-beginner-friendly`) — Understand it.** Get the ideas cold: the problem, the fix,
>   the trade-offs, with analogies and answer keys.
> - [`../plan-business-tutorial/`](../plan-business-tutorial/) — **Build it for real.** Implement the full,
>   secured Sales Order module end-to-end with your own hands (JWT, roles, credit limits, concurrency). Also
>   written for someone coming from **ASP.NET WebForms** (`.aspx.vb` + jQuery AJAX / `[WebMethod]`) — it
>   explains each decision by contrast with what you already know.
> - [`../plan-Opus/`](../plan-Opus/) — **Let AI build it.** A terse, AI-ready spec: hand a phase to a coding
>   agent (e.g. Claude Code), have it implement exactly that phase, and watch the architecture come together
>   — then read the real code against what you learned here. Same phase numbers line up.

---

## What are we building, in one sentence?

> The **Sales** part of a pretend company's ERP — the part that handles **customer orders** — built as a
> **modular monolith** so it's easy to understand, easy to test, and easy to grow into other departments
> later.

Think of a big company's software as a building with many departments: HR, Accounting, Purchasing,
Inventory, Sales. We're building the **Sales** department really well — as a self-contained **module** —
in a way that lets the other departments be added later as their own modules (or even split off into
separate programs someday).

## Why not just build it the quick way?

You *could* build ordering as a few web pages that talk straight to a database. That works until:

- The same rule ("a customer on credit hold can't order") gets copy-pasted into ten places and they
  slowly drift apart.
- You can't test "is this order allowed?" without spinning up a whole website and database.
- Every change means hunting through dozens of files.
- Two years in, everything touches everything, and no one dares move a table.

This project is a guided tour of techniques that fix those problems. The techniques have intimidating
names (you'll learn them), but each exists to solve a **real, specific pain** — and we always show you
the pain *before* the fix.

## The things you'll learn (with their scary names de-scared)

| Scary name | What it actually means | Everyday analogy |
|---|---|---|
| **Modular Monolith** | One deployable app, but split inside into self-contained **modules** (Sales, Inventory…) that don't reach into each other's guts. | One office building, but each department has its own locked floor. Still one building; clear walls inside. |
| **Vertical Slice Architecture (VSA)** | Keep all the code for one feature (like "create an order") together in one folder. | Organizing your kitchen by *meal* (a "tacos" drawer with everything for tacos) instead of by *type* (all knives here, all bowls there). |
| **Rich Domain Model** | Put business rules *inside* the objects they belong to (a `SalesOrder` knows how to submit itself). | A vending machine that refuses your money if it's out of stock — the *machine* enforces the rule; it doesn't trust the customer. |
| **CQRS-lite** | Use a rich, careful path for *changing* data and a simple, fast path for *reading* data. | A bank teller (careful, checks everything) vs. an ATM balance screen (just shows a number). |
| **Mediator** | A "post office": you drop off a request and it finds the right worker, so you don't have to know who that is. | Calling a company's main line and saying "billing please" instead of memorizing every desk phone. |
| **Carter + Minimal APIs** | A thin, tidy way to declare web endpoints next to the feature they belong to. | A reception desk on each department's floor, instead of one giant switchboard in the lobby. |
| **Stored procedures + Dapper** | Write your database queries as explicit SQL you can see, not hidden behind ORM magic. | Cooking from a written recipe instead of a mystery meal-kit. |

Don't worry if these don't fully click yet. Each gets its own phase.

## The one big idea: organize by *capability*, then by *feature*

Older tutorials (and the "Clean Architecture" style) split a solution into **technical layers**:
`Domain`, `Application`, `Infrastructure`, `Api` — four projects, and *one* feature is smeared across all
four. To change "create an order," you touch four projects.

We do it the other way. The top-level split is by **business capability** (a module: Sales, Inventory).
Inside a module, the split is by **feature** (create order, submit order, cancel order). To change
"create an order," you open **one folder**.

```
src/
├── Shared/            <- building blocks every module may use (mediator, base types, db plumbing)
├── Modules/
│   └── Sales/         <- the Sales department, a self-contained island
│       ├── Domain/        the SalesOrder object and its rules
│       ├── Features/      one folder per feature (CreateSalesOrder, SubmitSalesOrder, …)
│       ├── Infrastructure/ this module's database code
│       └── DependencyInjection.cs   how the module plugs into the app
└── Api/               <- the host that starts everything up
```

We still keep the *one genuinely good idea* from Clean Architecture — a **pure domain** you can test in
milliseconds — but we get it with folder discipline inside a module, not four ceremony projects. Phase 0
makes this concrete.

## How this whole thing is split up (the "phases")

We build in **10 phases**. Each phase is a lesson *and* a chunk of working software. Finish a phase, make
sure you understand it, then move on. **Do not skip ahead** — each phase assumes the one before it works.

| Phase | File | What you'll have when it's done | The big idea it teaches |
|---|---|---|---|
| 0 | [01-phase-0-orientation.md](01-phase-0-orientation.md) | Nothing yet — it's reading | How to *think* about where code goes |
| 1 | [02-phase-1-skeleton.md](02-phase-1-skeleton.md) | An empty-but-running app; one trivial "ping" feature via Carter | Where every file lives and why |
| 2 | [03-phase-2-mediator.md](03-phase-2-mediator.md) | The "post office" (mediator) built by hand | What problem the mediator solves |
| 3 | [04-phase-3-domain.md](04-phase-3-domain.md) | A smart `SalesOrder` object with real rules | Objects that protect their own rules |
| 4 | [05-phase-4-create-order.md](05-phase-4-create-order.md) | A working "create an order" feature, end to end | The shape every feature will copy |
| 5 | [06-phase-5-inventory-concurrency.md](06-phase-5-inventory-concurrency.md) | A second **Inventory** module; stock reservation; the "two people at once" problem | Module boundaries + which rules go in code vs. the database |
| 6 | [07-phase-6-queries.md](07-phase-6-queries.md) | Fast "read/search orders" features | Why reading is different from writing |
| 7 | [08-phase-7-remaining-slices.md](08-phase-7-remaining-slices.md) | Cancel, update, and "something happened" events | State changes as behavior |
| 8 | [09-phase-8-testing.md](09-phase-8-testing.md) | Automated tests at three levels | What each kind of test is *for* |
| 9 | [10-phase-9-modularity.md](10-phase-9-modularity.md) | Notes on splitting a module into its own service later | How to keep the "split later" option cheap |

### 📚 Extra help — read these too

- [**00a-FOUNDATIONS.md**](00a-FOUNDATIONS.md) — **New to C# or programming? START HERE, before Phase 0.**
  It explains the plain building blocks the phases assume: classes, interfaces, dependency injection,
  `async`/`await`, records, enums, exceptions, and symbols like `=>` and `?`. Zero prior knowledge
  assumed.
- [**GLOSSARY.md**](GLOSSARY.md) — every term in plain English with an analogy. Keep it open in a tab.
- [**LEARNING-RESOURCES.md**](LEARNING-RESOURCES.md) — when a concept won't click, this tells you exactly
  what to go practice and where.

Every phase ends with an **👨‍🏫 Answer Key** — full worked answers to its "Check Yourself" questions. Try
the questions first, *then* check yourself. If an answer surprises you, that's the exact section to
re-read.

## The tools we're using (and why)

| Choice | What it is | Why we picked it |
|---|---|---|
| **.NET 10** | The programming platform (C# language) | Current, long-term supported version. |
| **Modular monolith** | One app, split into isolated modules | The benefits of clear boundaries without the cost/complexity of microservices. |
| **Carter** | A thin wrapper over ASP.NET minimal APIs | Lets each feature declare its own endpoint next to its handler — VSA all the way to the web edge. |
| **FluentValidation** | A small library for input validation | The idiomatic way to say "this request must be well-formed" in one readable place. |
| **Dapper + stored procedures** | A lightweight way to run SQL you write yourself | You *see* every query. No hidden "magic" translating your code into SQL. |
| **No Entity Framework (EF)** | We skip the popular "ORM" | On purpose — ORMs hide the database, and right now we *want* to see it. |
| **Hand-rolled mediator** | We build our own ~150-line "post office" | Building it teaches the pattern far better than importing a library. |
| **SQL Server** | The database | Common in business/enterprise settings. |

> 🚫 **Deliberately NOT used:** Clean Architecture's four-project layering, EF Core, Docker, Redis,
> RabbitMQ, MassTransit, microservices. If you feel tempted to add one, that temptation is a topic for
> *after* this project — not during it.

## How to actually work through this

0. **Brand new to C#?** Read [00a-FOUNDATIONS.md](00a-FOUNDATIONS.md) first. It makes everything after
   readable.
1. **One phase at a time.** Read the phase, understand it, build it, *then* move on.
2. **Answer the "Check Yourself" questions**, then compare against the **Answer Key**. If you got it
   wrong or couldn't answer, re-read the section — the goal is understanding, not just code that runs.
3. **Resist adding extra stuff.** If you catch yourself (or an AI helper) adding caching, extra layers,
   or "just in case" features, stop. Every phase says clearly what's *out of scope*.
4. **Keep a decisions notebook.** When you make a choice that differs from the plan's default, write it
   down so later phases stay consistent.

## You'll truly "get it" when you can explain these five things without notes:

1. Why the "create an order" code and the "search orders" code are shaped *differently*.
2. For each business rule, whether it lives in the C# code or in the database — and why.
3. What the mediator (post office) gave you that a plain method call didn't — and what it cost.
4. Why the Sales module isn't allowed to `using` the Inventory module's internals — and how they talk
   instead.
5. What would have to change to spin the Sales module off into its own separate program.

If you can explain those five, you've learned what this project set out to teach.

Ready? Open [01-phase-0-orientation.md](01-phase-0-orientation.md).
