# Phase 9 — Keeping the "Split Into Services Later" Option Cheap

> 📄 Expert version: [`../plan-Opus/10-phase-9-modularity.md`](../plan-Opus/10-phase-9-modularity.md)

**In one sentence:** You already have a real modular monolith — two modules (`Sales`, `Inventory`) with
compiler-enforced walls, talking only through a contract. This phase is about what it takes to promote a
module into its *own program* someday, and why you shouldn't yet.

**What you'll build:** Mostly documentation and a light tidy-up: confirming the module-ownership rules,
writing an extraction checklist, and the outbox note.

**Before you start:** Phases 1–8. Both modules are complete and tested.

> 🧭 **Guiding principle:** *You do not build microservices you don't need.* This phase makes the *option* to
> split cheap — not mandatory. A modular monolith that *could* be split beats a distributed system you can't
> justify.

---

## Step 1 — What a "module" already is (you built this)

Unlike a folder-only convention, a module here is a **real project** (`Erp.Modules.Sales`,
`Erp.Modules.Inventory`) whose walls the *compiler* enforces — you proved this back in Phase 5 when Sales had
to reach Inventory through the `IStockReservation` contract because it *couldn't* reference Inventory
directly. A module is defined by three things it **owns**:

1. Its **own business objects** (`SalesOrder`; Inventory's `Stock`) — no other module references them.
2. Its **own features** (its commands and queries) and its **own DI registration** (`AddSalesModule`).
3. Its **own database schema and procs** (`sales.*`, `inventory.*`) — no other module queries them directly.

Cross-module contact happens through exactly two sanctioned doorways, both built already:
- a **synchronous contract** in Shared (`IStockReservation`) for "must happen in the same transaction," and
- an **integration event** (a notification another module subscribes to) for "react afterwards."

> 🔑 Because those three ownerships hold and the walls are compiler-enforced, splitting a module into a
> separate program later is *mechanical*. If you'd instead let Sales `JOIN` to `inventory.Stock` or share the
> `Stock` object, splitting would be painful surgery. **The discipline you already kept is what buys the
> cheap option.** *(A house with clean interior walls grows a second front door easily.)*

> ❓ **"If it's all one program, why not let modules share tables and objects? Less work today."** True — it
> *is* less work today. The cost is invisible until the day you want to move Inventory onto its own server
> because it's overloaded. If Sales had been `JOIN`-ing Inventory's tables, you couldn't move them without
> breaking Sales — the "quick split" becomes a multi-month rewrite. The discipline is a small continuous tax
> to keep a big option open.

## Step 2 — The extraction checklist (what it takes to make a module a service)

To promote `Inventory` from an in-process module into a standalone service someday:

1. **Move its project into its own solution/repo.** Because no other module referenced its internals (only
   the `IStockReservation` contract in Shared), nothing else depends on its types.
2. **It already has its own schema** (`inventory`). The tables/procs move with it — painless *because* no
   other module queried them directly.
3. **Replace the in-process contract implementation with a network client** — and adjust the contract's
   *shape*. `StockReservationService` (which ran SQL locally) becomes an HTTP/gRPC client. Sales still
   depends on an `IStockReservation`-shaped interface and its *call site* barely changes — but the interface
   itself must lose the `IDbTransaction tx` parameter, because a database transaction can't travel across a
   network. That's not a footnote; it's the crux of the next point.
4. **Turn the shared-transaction reserve into a saga.** Today submit *reserves stock and changes status in
   one transaction* — all-or-nothing for free. Across a service boundary you can't share that transaction,
   so "reserve" becomes a separate call, and if the rest of submit fails you must **compensate** by
   *releasing* what you reserved. This reserve→(commit | release) dance is the saga, and it's the real cost
   of the split — named honestly.
5. **Replace in-process integration events with real messaging** for cross-module reactions (Step 4).

> 💬 **The seams you already built pay off here.** Sales calls `IStockReservation` and speaks to the mediator
> in *messages*. Swapping the transport under those seams is localized — Sales' code barely changes. If Sales
> had `new StockReservationService(...)` or joined the tables, you'd rewrite every call site.

## Step 3 — The database boundary is the hard part (be honest)

The uncomfortable truth of "SQL Server + stored procs + could-become-services": **a shared database is the
thing that most often makes splitting expensive.** We avoided the trap by giving each module its own schema
and forbidding cross-schema joins — but the discipline must continue:

- Each module owns its schema. Cross-module reads go through the owning module's *queries/contract*, not a
  direct SQL join (Phase 6 covered this for reads).
- No foreign keys *across* module schemas (inside a module, use them freely). A cross-module reference is an
  id you resolve via the other module — not an FK you join on.
- Resist "just this one little join into `inventory.*`." It's the single most common way modular monoliths
  quietly fuse into an un-splittable big ball of mud.

> ⚖️ **This is a real cost you pay now** (some convenient joins become contract calls) for **an option you
> may never use.** If you're *certain* these will never be separate services, a shared schema is simpler. We
> assume they *might*, so we keep the boundary — a genuine cost, not free architecture.

## Step 4 — Cross-module communication, at scale

You've already used both doorways in-process. What changes at larger scale or after a split:

- **Commands/queries across modules** → the explicit contract (an interface now; HTTP/gRPC after a split).
  *Never* let module B build module A's objects or call its repository.
- **Reactions across modules** → an **integration event**, *not* the raw in-process domain event. An
  integration event is a *published contract* (versioned, serializable) — different from an internal domain
  event you can change freely. Keep them **separate types**, and consider a per-module `*.Contracts` project
  once you have several modules (the one shared `Contracts` folder we used is fine for two).
- **Reliable** cross-module reactions need a **transactional outbox**: write the event to an outbox table
  *in the same transaction* as the state change, then a separate relay publishes it with retries — because
  (Phase 7) post-commit in-process dispatch is best-effort. **Note it; don't build it unless you split.**

## Step 5 — What the current design already got right

- **Two modules with compiler-enforced walls**, communicating only through a Shared contract and events.
- **Per-module schemas** (`sales`, `inventory`) with no cross-schema joins — the data boundary is clean.
- **Aggregates as consistency boundaries** map cleanly onto future service boundaries.
- **No generic repository / no shared service layer** — no god-objects spanning modules to untangle.
- **Message-based call sites** (`ISender`) and the reservation contract make swapping transport localized.

## Step 6 — What you would deliberately NOT do

- **Don't split now.** No scaling, team-autonomy, or deployment pressure justifies the operational cost
  (network failures, distributed debugging, cross-service consistency).
- **Don't add a message broker "to be ready."** Add it when a real cross-module *reliable* reaction exists.
- **Don't distribute for its own sake.** A well-bounded modular monolith is the *right* architecture for the
  vast majority of systems this size. "Could be split later" is the feature — not "is split now."

## What to produce in this phase

- A short `MODULES.md` documenting the three ownership rules, the two sanctioned doorways, and the
  no-cross-schema-join rule.
- The extraction checklist (Step 2) and the outbox/integration-event note (Step 4), written as **design
  docs, not implementations**.

**NOT now:** actually extracting a module; adding a message broker; implementing an outbox. Documented as
*the path*, not built.

## Common mistakes to avoid

- **Premature extraction.** The most expensive mistake here.
- **Cross-schema joins / cross-module FKs.** The silent coupling that turns a future split into a rewrite.
- **Treating domain events as integration events.** Internal events change freely; published contracts must
  be versioned. Separate types.
- **Building the outbox/broker "just in case."** Add reliability machinery when a real requirement exists.

---

## ✅ Check Yourself

1. **What three ownerships define a module, and how did Phase 5 already prove the walls are real?**
2. **Why is the shared database (not the C# code) the hard part of splitting out a service?**
3. **What's the difference between a domain event and an integration event, and why must they be separate
   types?**
4. **Why is "a modular monolith that *could* split" usually the right answer over "microservices now"?**
5. **Which two seams you already built make changing the call transport localized?**

---

## 👨‍🏫 Answer Key

**1. Three ownerships, and how Phase 5 proved the walls.**
A module owns (1) its **business objects**, (2) its **features + DI**, and (3) its **schema + procs**, none
reached by other modules directly. Phase 5 proved it: Sales *could not* reference Inventory, so it had to
reserve through the `IStockReservation` contract — the compiler enforced the wall. That's why extraction is
mechanical rather than surgery.

**2. Why the database is the hard part.**
Clean C# moves easily, but a cross-schema `JOIN` from another module is a hidden coupling no tidy code fixes
— splitting the database breaks that query. The data boundary silently rots if you allow "just one join." So:
cross-module reads go through the owning module, and no cross-schema FKs.

**3. Domain vs. integration event, and why separate types.**
A **domain event** is *internal* — you change it freely because only your module reads it. An **integration
event** is a *published contract* other modules/services depend on, so it must be versioned and stable. Keep
them separate types, or subscribers get coupled to your internals and you can't evolve your internal events.

**4. Why "could split" over "microservices now"?**
Splitting buys real costs *immediately* — network failures, distributed debugging, cross-service consistency,
deployment complexity — and the benefits (independent scaling, team autonomy) only pay off under genuine
pressure most systems this size don't have. A well-bounded modular monolith gives clean structure now and
keeps the split *option* cheap. "Could split later" is the feature.

**5. Which two seams localize a transport change?**
The **mediator** (`ISender.Send`) and the **cross-module contract** (`IStockReservation`). Callers speak in
messages and depend on an interface, so the implementation under each seam can switch from in-process to a
network call with call sites largely untouched.

---

## 🎉 You've reached the end

If you've worked through all ten phases and can answer the check-yourself questions, come back to
[00-START-HERE.md](00-START-HERE.md) and try the five big questions at the bottom. If you can explain those
without notes, this project has done its job — you don't just have working software, you understand *why*
every piece is shaped the way it is. When you want the deeper "why" behind any decision (especially coming
from WebForms), the [business tutorial](../plan-business-tutorial/) is your companion read.
