# 00 — Overview & Architecture Decisions

This is the master document. Read it once, fully, before starting Phase 0. It holds
every cross-cutting decision so the phase files don't repeat themselves. Phase files
*reference* decisions made here rather than re-arguing them.

> **How to read this set:** one file per phase. Each phase file is self-contained enough
> to hand to an implementer (e.g. Sonnet) as "build exactly this, nothing more." You review
> against the phase's **Checkpoint** section before moving on. Do not let the implementer
> skip ahead — the whole point is that each phase is understandable in isolation.

---

## 1. What we are actually building

An **Order module** for a fictional enterprise ERP. One department (Ordering), modelled
so that HR / Accounting / Manufacturing could later become separate modules or services.

The domain has real rules (not CRUD):

- Create an order (draft) with line items
- Price it (line prices, discounts, totals)
- Validate the customer (credit hold, active status)
- Validate inventory availability
- Reserve inventory, then deduct stock on booking
- Book an order (draft → booked) with an approval rule for large orders
- Cancel an order (releases reservations), with rules about *which* states can cancel
- Concurrency + transactions around the money-and-stock operations

That list is deliberately chosen so that "where does this logic go?" has a *non-obvious*
answer. If everything were CRUD, the architecture would look like pointless ceremony.

---

## 2. Verified facts (checked, with dates)

- **.NET 10** — released Nov 2025, LTS, current as of this writing. Baseline for the solution. *(fact)*
- **MediatR licensing** — MediatR moved to a **dual license** on **2 July 2025** (Lucky Penny Software).
  It remains **free** for individuals and companies under **$5M USD revenue**; larger commercial
  use requires a paid license. It does not hard-block usage without a key — it logs a prompt. *(fact)*
  - Implication: for a learning project MediatR would actually be free. We still avoid it, per your
    brief, and because **hand-rolling teaches the pattern better than importing it.**
- **Free MediatR-shaped library** — `martinothamar/Mediator` (source-generator based, MediatR-like API,
  MIT — *verify the LICENSE file yourself*). Caveats: performance degrades past ~500 message types
  (being addressed in v3), no generic requests/notifications, returns `ValueTask`. *(fact + caveat)*
- **Recommendation** — we **hand-roll a ~150-line mediator** in Phase 2 (MediatR-compatible interface
  names so the mental model transfers), and note `martinothamar/Mediator` as the drop-in replacement
  if you later stop wanting to maintain your own. *(opinion, reasoned)*

---

## 3. The four tensions in your brief (read this carefully)

Your requirements are mostly coherent, but four pairs of them pull against each other. Each is
resolved below with a default you can override. These are the decisions most likely to confuse a
learner if left implicit.

### Tension A — Rich DDD domain **vs.** "business rules stay in SQL" *(most important)*

If you put order/booking rules in **both** stored procedures **and** domain entities, you get
*split-brain logic*: two sources of truth that drift apart. This is the single most common way
"DDD + Dapper + stored procs" projects rot.

**Resolution — ownership split, no duplication:**

| Concern | Owner | Why |
|---|---|---|
| Invariants & decisions ("can this order be cancelled?", "is this status transition legal?", "is quantity > 0?") | **C# domain layer** | Pure, unit-testable with zero DB, and the *reason the domain layer exists*. |
| Persistence, mapping, set-based reads | **Stored procedures / Dapper** | That's what SQL is good at. |
| **Atomic concurrency guards** ("deduct stock only if enough remains", optimistic version check) | **Stored procedures** | Under concurrency this *must* be atomic at the DB, or you get race conditions. See Tension A.1. |
| Data-integrity constraints (FKs, NOT NULL, CHECK) | **SQL schema** | Enforced regardless of which app touches the DB. |

**Rule of thumb:** *policy* lives in the domain; *integrity and atomicity* live in SQL. When a rule
looks like it belongs in both, ask "does concurrency make the in-memory check unsafe?" If yes → SQL.
If no → domain.

#### Tension A.1 — the one legitimate overlap

"Reserve inventory only if `Available >= Requested`" *feels* like a business rule that belongs in the
domain. But if two orders check availability in memory at the same time, both pass, both reserve, and
you oversell. So the **final** guard is a single atomic SQL statement:

```sql
UPDATE Inventory
SET Reserved = Reserved + @Qty
WHERE MaterialId = @MaterialId
  AND (OnHand - Reserved) >= @Qty;   -- returns 0 rows affected if not enough
```

The domain still *expresses* the rule (for fast feedback and testing); SQL *enforces* it under
concurrency. This is a teaching highlight, not an accident — Phase 5 dwells on it.

### Tension B — Generic `TId` **vs.** "avoid unnecessary abstraction"

Your brief asks for a generic `TId` that lets you swap `int`/`long`/`Guid`/`ULID`. A single generic
type parameter threaded through every entity, handler, repository, and Dapper mapping is **viral** —
it complicates signatures everywhere and fights your own "avoid unnecessary abstraction" and
"simple/maintainable" goals.

**Resolution — strongly-typed IDs per aggregate, not a global generic:**

```csharp
public readonly record struct OrderId(long Value);
public readonly record struct CustomerId(long Value);
```

This gets you *more* than the brief asked for:

- **Type safety** — you cannot pass a `CustomerId` where an `OrderId` is expected (a `long`/`Guid`
  would let you). This alone catches a whole class of bugs.
- **Swap the backing type** — change `long` to `Guid`/`ulong`/etc. in **one line per aggregate**,
  plus one Dapper `TypeHandler`. No viral generic parameter.

**Cost (stated honestly):** each strongly-typed ID needs a Dapper `SqlMapper.TypeHandler<T>` so it maps
to/from the column type. That's ~10 lines per ID type. If you truly want the *literal* generic `TId`,
Phase 3 notes how, but the default is strongly-typed IDs. **This is a direct override of your brief;
decide before Phase 3.**

### Tension C — VSA + Clean Architecture + DDD together

They don't compose as cleanly as tutorials imply:

- **VSA** says: organize by feature, minimize shared layers, keep a feature's code together.
- **Clean Architecture** says: enforce a layered dependency rule (domain depends on nothing).
- **DDD** adds a domain layer shared across features.

The conflict: `CreateOrder`, `BookOrder`, and `CancelOrder` all operate on the **same `Order`
aggregate**. Pure VSA would duplicate or trap the aggregate inside one slice; that's wrong — the
aggregate is genuinely shared.

**Resolution — pragmatic hybrid (this is the project's spine):**

- **Domain** is a shared project (the `Order` aggregate, value objects, domain services). Depends on nothing.
- **Application** is organized **vertically by feature** — each feature folder owns its command/query,
  validator, handler, and any feature-specific DTOs.
- **Infrastructure** (Dapper, connection factory, stored-proc callers, dispatchers) is shared.
- **Api** is thin — endpoints call `ISender.Send(command)` and return the result.

So: **layers for the things that are genuinely shared (domain, infra), slices for the things that
are per-feature (application handlers).** Phase 1 draws the exact folder tree and defends every folder.

### Tension D — "CQRS" here is CQRS-lite

Full CQRS = separate read and write **models/stores**, often with event sourcing and eventual
consistency. That is a large, operationally heavy commitment and **not** what this project needs.

**What we build (CQRS-lite):**

- Separate **command** handlers (go through the domain, enforce rules, write) and **query** handlers
  (bypass the domain, read straight into DTOs via Dapper).
- **Same** SQL Server database for both.

**What we deliberately do NOT build:** separate read store, projections, event sourcing, eventual
consistency. Phase 6 names exactly what full CQRS would add and when it's worth it, so you don't
conflate the two.

---

## 4. Technology decisions (summary)

| Decision | Choice | Rationale |
|---|---|---|
| Runtime | .NET 10 (LTS) | Current, supported. |
| Data access | Dapper + stored procedures | Per brief. Explicit SQL, no ORM magic; you see every query. |
| ORM | **None** (no EF) | Per brief; also removes the change-tracker crutch so persistence is explicit. |
| Mediator | **Hand-rolled** (MediatR-shaped API) | Free, teaches the pattern, no license risk. |
| IDs | Strongly-typed per aggregate | See Tension B. |
| Validation | FluentValidation (MIT) *or* hand-rolled | Phase 4 decides; FluentValidation is free and idiomatic. |
| Repository | **Per-aggregate** repositories, **no generic repository** | Per brief; generic repos leak persistence concerns and encourage anemic domains. |
| Services | Domain services **only** when logic spans aggregates | Per brief; "when NOT to create a service" is a Phase 3/5 topic. |
| Transactions | **Explicit** in command handlers (not hidden in a pipeline behavior) | So the transaction boundary is visible while learning. See Phase 4. |

---

## 5. Phase map

| Phase | File | Produces | Core lesson |
|---|---|---|---|
| 0 | `01-phase-0-orientation.md` | Nothing (reading) | WebForms → VSA mental model; glossary |
| 1 | `02-phase-1-skeleton.md` | Solution, projects, folders, one trivial end-to-end slice | Where code lives and why; walking skeleton |
| 2 | `03-phase-2-mediator.md` | Hand-rolled mediator + pipeline behaviors | What the mediator solves; before/after |
| 3 | `04-phase-3-domain.md` | `Order` aggregate, value objects, IDs, domain events (defined) | Rich vs anemic; where domain logic lives |
| 4 | `05-phase-4-create-order.md` | `CreateOrder` slice end-to-end (Dapper + proc + tx) | Anatomy of a command slice; validation placement |
| 5 | `06-phase-5-inventory-concurrency.md` | Reservation, stock deduction, pricing, approval, `BookOrder`, concurrency | Atomicity vs policy; SQL vs domain |
| 6 | `07-phase-6-queries.md` | `GetOrder`, `SearchOrders` read slices | Why queries bypass the domain (CQRS-lite) |
| 7 | `08-phase-7-remaining-slices.md` | `CancelOrder`, `UpdateCustomer`, status state machine, event dispatch | State transitions as domain behavior |
| 8 | `09-phase-8-testing.md` | Unit, integration, e2e tests | What each test level is *for* |
| 9 | `10-phase-9-modularity.md` | Module boundaries, extraction notes | How this becomes a service later |

---

## 6. How to drive this with Sonnet (practical)

1. **One phase per session.** Paste the phase file and say: *"Implement exactly this phase. Do not
   build anything from later phases. Stop at the Checkpoint and summarize what you built and why."*
2. **Review against the Checkpoint** questions before continuing. If you can't answer them, the
   phase didn't teach — ask for the explanation, not more code.
3. **Keep a decisions log.** When you override a default here (e.g. you *do* want literal `TId`),
   record it so later phases stay consistent.
4. **Resist scope creep.** If Sonnet starts adding caching, MediatR, EF, or a generic repository,
   that's a signal it drifted from the brief — pull it back.
5. **Build order is dependency order.** Phases assume the previous one compiles and its Checkpoint passed.

---

## 7. Success criteria for the whole project

You're done when you can, without notes, explain:

- Why a command slice and a query slice are shaped differently.
- Which of the twelve business operations has its rule in the domain, and which in SQL, and *why*.
- What the mediator gave you that a direct method call didn't — and what it cost.
- Where you would (and would not) add a domain service.
- What would have to change to split the Order module into its own service.

If you can explain those five, the architecture has done its teaching job.
