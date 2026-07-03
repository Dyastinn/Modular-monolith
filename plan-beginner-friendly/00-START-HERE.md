# 00 — Start Here (Read This First)

Welcome. This folder is the **plain-English, beginner-friendly** version of the plan in
[`../plan-Opus/`](../plan-Opus/). The original plan is excellent, but it's written for someone who
already knows a lot of architecture vocabulary. This version explains the same ideas using everyday
language, analogies, and lots of "here's *why*."

**Nothing here contradicts the original plan.** Same project, same decisions, same code. We just slow
down and explain.

> **If you ever want the terse, expert version of any phase, open the matching file in
> [`../plan-Opus/`](../plan-Opus/).** The file numbers line up: `01-...` here matches `01-...` there.

---

## What are we building, in one sentence?

> A small piece of a pretend company's software — the part that handles **customer orders** — built
> carefully so it's easy to understand, easy to test, and easy to grow later.

Think of a big company's software as a building with many departments: HR, Accounting, Manufacturing,
Ordering. We're building the **Ordering** department really well, in a way that would let the other
departments be added later (or even split off into their own separate programs someday).

## Why not just build it the quick way?

You *could* build ordering as a few web pages that talk straight to a database. That works until:

- The same rule ("a customer on credit hold can't order") gets copy-pasted into ten places and they
  slowly drift apart.
- You can't test "is this order allowed?" without spinning up a whole website and database.
- Every change means hunting through dozens of files.

This project is a guided tour of a set of techniques that fix those problems. The techniques have
intimidating names (you'll learn them), but each one exists to solve a **real, specific pain** — and
we always show you the pain *before* we show you the fix.

## The things you'll learn (with their scary names de-scared)

| Scary name | What it actually means | Everyday analogy |
|---|---|---|
| **Domain-Driven Design (DDD)** | Put business rules inside the objects they belong to (an `Order` knows how to book itself). | A vending machine that refuses your money if it's out of stock — the *machine* enforces the rule, you don't trust the customer to. |
| **Vertical Slice Architecture (VSA)** | Keep all the code for one feature (like "create an order") together in one folder. | Organizing your kitchen by *meal* (a "taco" drawer with everything for tacos) instead of by *type* (all knives here, all bowls there). |
| **Clean Architecture** | Make sure the important business code doesn't depend on details like the database or the web framework. | The recipe doesn't care what brand of oven you own. |
| **CQRS-lite** | Use a rich, careful path for *changing* data and a simple, fast path for *reading* data. | A bank teller (careful, checks everything) vs. an ATM balance screen (just shows a number). |
| **Mediator** | A "post office": you drop off a request and it finds the right worker to handle it, so you don't have to know who that is. | Calling a company's main line and saying "billing please" instead of memorizing every employee's desk phone. |
| **Stored procedures + Dapper** | Write your database queries as explicit SQL you can see, not hidden behind ORM magic. | Cooking from a written recipe instead of a mystery meal-kit. |

Don't worry if these don't fully click yet. Each gets its own phase.

## How this whole thing is split up (the "phases")

We build in **10 phases**. Each phase is a lesson *and* a chunk of working software. You finish a
phase, make sure you understand it, then move on. **Do not skip ahead** — each phase assumes the one
before it works.

| Phase | File | What you'll have when it's done | The big idea it teaches |
|---|---|---|---|
| 0 | [01-phase-0-orientation.md](01-phase-0-orientation.md) | Nothing yet — it's reading | How to *think* about where code goes |
| 1 | [02-phase-1-skeleton.md](02-phase-1-skeleton.md) | An empty-but-running app; one trivial "ping" feature | Where every file lives and why |
| 2 | [03-phase-2-mediator.md](03-phase-2-mediator.md) | The "post office" (mediator) built by hand | What problem the mediator solves |
| 3 | [04-phase-3-domain.md](04-phase-3-domain.md) | A smart `Order` object with real rules | Objects that protect their own rules |
| 4 | [05-phase-4-create-order.md](05-phase-4-create-order.md) | A working "create an order" feature, end to end | The shape every feature will copy |
| 5 | [06-phase-5-inventory-concurrency.md](06-phase-5-inventory-concurrency.md) | Booking, stock reservation, the tricky "two people at once" problem | Which rules go in code vs. the database |
| 6 | [07-phase-6-queries.md](07-phase-6-queries.md) | Fast "read/search orders" features | Why reading is different from writing |
| 7 | [08-phase-7-remaining-slices.md](08-phase-7-remaining-slices.md) | Cancel, update, and "something happened" events | State changes as behavior |
| 8 | [09-phase-8-testing.md](09-phase-8-testing.md) | Automated tests at three levels | What each kind of test is *for* |
| 9 | [10-phase-9-modularity.md](10-phase-9-modularity.md) | Notes on splitting into services later | How to keep the "split later" option cheap |

There's also a [**GLOSSARY.md**](GLOSSARY.md) — every term in plain English. Keep it open in a tab.

## The tools we're using (and why)

| Choice | What it is | Why we picked it |
|---|---|---|
| **.NET 10** | The programming platform (C# language) | Current, long-term supported version. |
| **Dapper + stored procedures** | A lightweight way to run SQL you write yourself | You *see* every query. No hidden "magic" translating your code into SQL. |
| **No Entity Framework (EF)** | We're skipping the popular "ORM" | On purpose — ORMs hide the database, and right now we *want* to see it. |
| **Hand-rolled mediator** | We build our own ~150-line "post office" | Building it teaches the pattern far better than importing a library. |
| **SQL Server** | The database | Common in business/enterprise settings. |

## How to actually work through this

1. **One phase at a time.** Read the phase, understand it, build it, *then* move on.
2. **Answer the "Check Yourself" questions** at the end of each phase before continuing. If you can't,
   re-read — the goal is understanding, not just having code that runs.
3. **Resist adding extra stuff.** If you catch yourself (or an AI helper) adding caching, extra
   libraries, or "just in case" features, stop. Every phase says clearly what's *out of scope*.
4. **Keep a decisions notebook.** When you make a choice that differs from the plan's default, write
   it down so later phases stay consistent.

## You'll truly "get it" when you can explain these five things without notes:

1. Why the "create an order" code and the "search orders" code are shaped *differently*.
2. For each business rule, whether it lives in the C# code or in the database — and why.
3. What the mediator (post office) gave you that a plain method call didn't — and what it cost.
4. Where you *would* and *wouldn't* add a "service" class.
5. What would have to change to spin the Ordering department off into its own separate program.

If you can explain those five, you've learned what this project set out to teach.

Ready? Open [01-phase-0-orientation.md](01-phase-0-orientation.md).
