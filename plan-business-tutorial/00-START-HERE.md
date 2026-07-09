# 00 — Start Here: Build a Real ERP Sales Order Module

> 👨‍🏫 **From your senior:** This is the real thing — a **production-shaped Sales Order module** for an ERP,
> with the messy business rules actual companies have: credit limits, blocked customers, inventory
> reservation, approval workflows, role-based security. We build it end-to-end, and **everything you need is
> right here** — every command, every explanation. No prior reading required; we explain each idea as we use
> it.

---

## This is the main course — and it stands on its own

This folder is **the course**: you build the whole module yourself, and every step you need lives here.
It doesn't send you to another folder to create the solution or to understand a concept before you can
continue — when we introduce a mediator, a value object, an atomic SQL guard, or a validation behavior, we
explain it right where we use it and then show it playing out in a real business feature.

Two **optional** companions cover the *same* architecture from other angles. You never *need* them, but
they're there when you want a different view:

- [`../plan-beginner-friendly/`](../plan-beginner-friendly/) — **Understand it (optional, theory-first).**
  A lightweight, analogy-rich read on *what problem* each pattern solves — mediator, domain, CQRS, vertical
  slices, testing. Great on a commute, and the place to go if a concept here feels abstract and you want the
  slow version. New to C# itself? [00a-FOUNDATIONS.md](../plan-beginner-friendly/00a-FOUNDATIONS.md) there is
  a gentle on-ramp.
- [`../plan-Opus/`](../plan-Opus/) — **Let AI build it (optional).** The terse, AI-ready spec of the same
  phases. If you'd rather *watch* a phase get built than type it, hand it to a coding agent (e.g. Claude
  Code), then compare the result to what you'd have written here.

> 📎 Whenever you see a 📎 link like [beginner Phase 5](../plan-beginner-friendly/06-phase-5-inventory-concurrency.md),
> that's a purely **optional** "go here if you want the slow, analogy-rich version of this idea" — never a
> step you must complete first.

---

## What we're building

A **Sales Order module** — the part of an ERP where a salesperson creates an order for a customer, it
gets checked against the business rules, a manager approves it, and inventory is reserved so the warehouse
can fulfill it. By the end you'll have secured HTTP endpoints for:

| Endpoint | Who can call it | What it does |
|---|---|---|
| `POST /auth/login` | anyone | Log in, get a short-lived access token + a refresh token |
| `POST /auth/refresh` | anyone with a valid refresh token | Rotate the refresh token, get a fresh access token |
| `POST /auth/logout` / `/auth/logout-all` | valid refresh token / any logged-in user | Revoke this session / every session |
| `POST /sales-orders` | SalesRep, SalesManager | Create a draft order (with all the create-time checks) |
| `PUT /sales-orders/{id}` | SalesRep, SalesManager | Edit a *draft* order's lines |
| `POST /sales-orders/{id}/submit` | SalesRep, SalesManager | Submit for approval + **reserve inventory** |
| `POST /sales-orders/{id}/approve` | **SalesManager only** | Approve a submitted order |
| `POST /sales-orders/{id}/cancel` | SalesRep, SalesManager | Cancel + **release reservations** |
| `GET /sales-orders/{id}` | any logged-in user | Get one order's full detail |
| `GET /sales-orders` | any logged-in user | Search/filter/page through orders |

> 📝 **A note on role names.** There are exactly three roles in this system, and these are their *literal*
> strings — the same text stored in the database and baked into the JWT: **`SalesRep`**, **`SalesManager`**,
> and **`Admin`**. When this tutorial says "a manager" in plain English, the actual role string is always
> **`SalesManager`** (never just `"Manager"`). We use the exact strings everywhere in code so a typo can't
> silently break an authorization check.

## The business rules we'll enforce

This is the whole point — none of it is CRUD. Each rule is a decision about *where the logic lives*, which
is the skill the earlier plans trained. You'll learn exactly where each one belongs:

1. **JWT authentication** — you must log in to touch any sales-order endpoint.
2. **Role-based authorization** — only a **SalesManager** can approve orders.
3. **Customer blocking** — you cannot create or submit an order for a *blocked* customer.
4. **Credit limit** — an order can't push the customer's balance past their credit limit.
5. **Inventory availability & reservation** — you can only submit if stock is available, and submitting
   *atomically reserves* it so two salespeople can't reserve the same last unit (the concurrency lesson
   from [beginner Phase 5](../plan-beginner-friendly/06-phase-5-inventory-concurrency.md)).
6. **Item category restrictions** — some categories (e.g. "Controlled") are limited to certain roles,
   and each category caps how many units can go on a single order.
7. **Approval workflow** — every submitted order goes to a manager for approval before it becomes
   Approved, moving through a proper **state machine**; orders over a value threshold are additionally
   **flagged as high-value** (`RequiresApproval`) so managers can prioritise scrutiny. *(Auto-approving
   small orders is a deliberate [extension exercise](13-wrap-up-checklist.md), not the base flow — see the
   ⚠️ note in [Phase 5 §8](05-domain-model.md).)*
8. **Cancellation rules** — only certain statuses can be cancelled, and cancelling *releases* any
   reservation so stock isn't lost forever.
9. **Transaction rollback on failure** — every multi-step write (reserve 5 lines + change status) is
   wrapped in a SQL transaction, so a failure halfway through undoes *everything*.

## The exact stack (identical to the earlier plans — nothing new)

| Concern | Choice | Why (see earlier plans) |
|---|---|---|
| Runtime / language | **.NET 10**, C# | Current LTS. |
| Architecture | **Modular Monolith + Vertical Slice Architecture** | Split by capability (module), then by feature — not by technical layer. |
| Web | **Carter** (over ASP.NET minimal APIs) | Each feature declares its own endpoint next to its handler. |
| Data access | **Dapper + stored procedures** | Explicit SQL you can see; no ORM magic. |
| ORM | **None (no EF Core)** | On purpose — persistence stays explicit. |
| Mediator | **Hand-rolled** (we keep it hand-rolled; the free **martinothamar/Mediator** is compared in [Phase 02](02-mediator-own-vs-free.md)) | Building it teaches the pattern; no paid license. |
| Request style | **CQRS-lite + Vertical Slices** | Commands change state through the domain; queries read straight via Dapper. |
| Validation | **FluentValidation** | Idiomatic input validation in a pipeline behavior. |
| Security | **JWT bearer tokens + role auth** | Standard, built into ASP.NET Core. |
| Database | **SQL Server** (schema per module) | Common in enterprise ERP; `sales` / `inventory` schemas echo the module walls. |
| Transactions | **Explicit SQL transactions** in handlers | Boundary stays visible while learning. |

> 🚫 **Deliberately NOT used** (and we never sneak them in): Clean Architecture's four-layer project split,
> Docker, Redis, EF Core, RabbitMQ, MassTransit, microservices, or any library the earlier plans didn't
> cover. If you ever feel tempted to reach for one, that temptation is a topic for *after* this tutorial —
> not during it.

> 🧭 **Coming from WebForms (`.aspx.vb` + jQuery AJAX / `[WebMethod]`)?** This tutorial is written for you.
> At each step we contrast the new way with what you already know: a `[WebMethod]` becomes a **Carter
> endpoint**, a code-behind `Button_Click` becomes a **handler**, `SqlCommand`/`DataSet` becomes **Dapper +
> a stored proc**, and the business rules you used to scatter across pages move into a **rich domain
> model**. Watch for the 🧭 markers — they're your "translation from WebForms" notes.

## The roadmap (each file is one phase)

Everything below is self-contained — the last column is just **optional** companion reading for anyone who
wants the slow, analogy-rich take on a concept, never a prerequisite.

| Phase | File | What you'll build | ⏱️ Est. time | Optional deeper reading |
|---|---|---|---|---|
| 0 | **this file** | The plan and the business rules | ~20 min (read) | — |
| 1 | [01-solution-setup.md](01-solution-setup.md) | Projects, folders, packages, config | ~1 hr | [beginner Phase 1](../plan-beginner-friendly/02-phase-1-skeleton.md) |
| 2 | [02-mediator-own-vs-free.md](02-mediator-own-vs-free.md) | Your own mediator **vs** the free GitHub one — decide | ~1 hr | [beginner Phase 2](../plan-beginner-friendly/03-phase-2-mediator.md) |
| 3 | [03-database-schema.md](03-database-schema.md) | Tables, stored procedures, seed data | ~1–1.5 hr | [beginner Phase 4 §6](../plan-beginner-friendly/05-phase-4-create-order.md) |
| 3b | [03b-data-access.md](03b-data-access.md) | Connection factory + ADO.NET transactions | ~30–45 min | [beginner Phase 4](../plan-beginner-friendly/05-phase-4-create-order.md) |
| 4 | [04-jwt-auth.md](04-jwt-auth.md) | **Part 1** — password hashing + the access-token service | ~45 min *(new topic)* | *new topic* |
| 4b | [04b-jwt-login.md](04b-jwt-login.md) | **Part 2** — login slice, token validation, `ICurrentUser`, role authorization | ~1 hr *(new topic)* | *new topic* |
| 4c | [04c-jwt-refresh-apikey.md](04c-jwt-refresh-apikey.md) | **Part 3** — rotating refresh tokens (reuse detection) + the API-key gate | ~45 min *(new topic)* | *new topic* |
| 4d | [04d-jwt-change-password.md](04d-jwt-change-password.md) | **Part 4** — change/reset password over the DB proc (and why it's anonymous) | ~30 min *(new topic)* | *new topic* |
| 4e | [04e-jwt-wrap-up.md](04e-jwt-wrap-up.md) | **Part 5** — full request-flow trace, checkpoint, common mistakes | ~20 min *(new topic)* | *new topic* |
| 5 | [05-domain-model.md](05-domain-model.md) | The `SalesOrder` aggregate + business rules | ~1.5–2 hr | [beginner Phase 3](../plan-beginner-friendly/04-phase-3-domain.md) |
| 6 | [06-create-order.md](06-create-order.md) | Create-order slice: blocking, credit, category, quantity | ~2 hr | [beginner Phase 4](../plan-beginner-friendly/05-phase-4-create-order.md) |
| 7 | [07-submit-reserve-concurrency.md](07-submit-reserve-concurrency.md) | Submit + atomic inventory reservation + rollback | ~1.5–2 hr | [beginner Phase 5](../plan-beginner-friendly/06-phase-5-inventory-concurrency.md) |
| 8 | [08-approval-cancellation.md](08-approval-cancellation.md) | Approve (manager), cancel (release), state machine | ~2 hr | [beginner Phase 7](../plan-beginner-friendly/08-phase-7-remaining-slices.md) |
| 9 | [09-update-order.md](09-update-order.md) | Update a draft order's lines | ~45 min–1 hr | [beginner Phase 4](../plan-beginner-friendly/05-phase-4-create-order.md) |
| 10 | [10-queries-search.md](10-queries-search.md) | Get one + search/filter/page | ~1–1.5 hr | [beginner Phase 6](../plan-beginner-friendly/07-phase-6-queries.md) |
| 11 | [11-cross-cutting.md](11-cross-cutting.md) | Validation, global error handling, logging | ~1–1.5 hr | [beginner Phase 2](../plan-beginner-friendly/03-phase-2-mediator.md) |
| 12 | [12-testing.md](12-testing.md) | Unit + integration tests for the business rules | ~2 hr | [beginner Phase 8](../plan-beginner-friendly/09-phase-8-testing.md) |
| 13 | [13-wrap-up-checklist.md](13-wrap-up-checklist.md) | Recap + production-readiness checklist | ~30 min (read) | — |

> ⏱️ **Total: roughly 18–22 hours of focused, hands-on time** — think two to three unhurried days, or a
> couple of evenings a week for a few weeks. These are *typing-and-understanding* estimates for someone
> comfortable with C#, **not** counting deep debugging detours (which are where a lot of the real learning
> happens — budget slack). Newer to C#? Add 50–100% and lean on the linked beginner phases. The phases are
> sized to be **one sitting each**; Phases 4–8 are the meatiest, so start them when you're fresh.

## Every phase follows the same shape

Nothing gets skipped and nothing is assumed. **Every phase answers the same five questions:**

1. **What we're building** — the concrete deliverable of this phase.
2. **Why it's designed this way** — the reasoning, linking back to the pattern's home in the other plans.
3. **How the request flows** — a step-by-step trace of a real request through the code.
4. **Why each class/component exists** — no mystery files; every piece is justified.
5. **Common mistakes & best practices** — the traps beginners fall into, and how to avoid them.

## How to work through it

- **Go in order.** Each phase assumes the previous one compiles and works.
- **Type the code, don't paste it.** Fixing the compiler's complaints is where the learning happens
  (see [beginner LEARNING-RESOURCES](../plan-beginner-friendly/LEARNING-RESOURCES.md)).
- **When a concept feels shaky,** open the linked beginner phase for the slow version, then come back.
- **Resist scope creep.** If you catch yourself adding a cache or a queue "to be safe," stop — that's
  not this tutorial.

> 🖥️ **You don't have to build a client to test your work.** A **ready-made test console** — a single
> standalone [`test-console/index.html`](../course-validation/business/test-console/index.html) you just
> open from disk while the API runs — lets you drive create → edit → submit → approve → cancel from a
> browser (every one of the sales-order endpoints), watching each rule fire, without writing any front-end. There's also Swagger at `/swagger`. A complete,
> runnable reference implementation of this whole tutorial lives in
> [`../course-validation/business/`](../course-validation/business/); see the
> [repo README](../README.md#running-the-reference-build) for the one-command run. Build it yourself first —
> the console is there to *exercise* what you build, not replace the typing.

Ready. Open [01-solution-setup.md](01-solution-setup.md).
