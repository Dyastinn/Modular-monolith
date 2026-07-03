# Phase 9 — Keeping the "Split Into Services Later" Option Cheap

> 📄 Expert version: [`../plan-Opus/10-phase-9-modularity.md`](../plan-Opus/10-phase-9-modularity.md)

**In one sentence:** We make sure the Ordering module *could* become its own separate program someday
— without actually splitting anything now — by understanding what keeps that option cheap and what
would quietly make it expensive.

**What you'll build:** Mostly documentation and a light, no-behavior-change tidy-up: module-boundary
rules, an extraction checklist, and notes on cross-module communication.

**Before you start:** Phases 1–8. The Order module is complete and tested.

> 🧭 **Guiding principle:** *You do not build microservices you don't need.* This phase makes the
> *option* to split cheap — not mandatory. A modular monolith that *could* be split later beats a
> distributed system you can't justify. Say that out loud to resist the temptation to split early.

---

## Step 1 — What "module" means today

Right now, a "module" is just a **folder convention** inside the shared projects: an `Orders` folder
under `Domain`, `Application`, `Infrastructure`, and `Api`. That's deliberate — folder boundaries are
free to rearrange and cost nothing to deploy. A module is defined by three things it *owns*:

1. Its **own business objects** (`Order`, `Customer`, `Material`, `Inventory`) — no other module reaches
   inside them.
2. Its **own features** (its commands and queries).
3. Its **own tables and procs** — no other module queries the Order module's tables directly.

> 🔑 If those three hold, splitting the module into a separate program later is *mechanical*. If they've
> been violated (say, the HR module `JOIN`s to the `Order` table, or shares the `Order` object),
> splitting becomes painful surgery. **The discipline you keep *now* is what buys the cheap option
> *later*.** *(A house with clean interior walls can grow a second front door easily; a house with
> plumbing threaded randomly through every wall can't.)*

## Step 2 — The extraction checklist (what it takes to make it a service)

To promote `Orders` from a folder-module into a standalone service someday:

1. **Move its projects/folders into their own solution** (`Orders.Domain`, `Orders.Application`, …).
   Because you never shared business objects across modules, nothing else depends on these types.
2. **Give it its own database** (or schema). The Order tables/procs move with it — painless *only if*
   no other module was querying them directly (see Step 3).
3. **Replace in-process calls with a network boundary** (HTTP/gRPC) for anything another module needs
   from Orders. Calls that used to be `sender.Send(...)` in-process become network calls.
4. **Replace in-process domain events with real messaging** for cross-module reactions (Step 4).
5. **Handle the data you used to join to** — anything another module read from Order tables now needs
   an API call or a copied read model.

> 💬 **The mediator pays off here.** Because callers already speak in terms of *messages*
> (`Send(command)`), the transport underneath can switch from in-process to network with the call sites
> largely unchanged. That's a *real, concrete* benefit of Phase 2 you can now point to — not the whole
> reason for the mediator, but a genuine one.

## Step 3 — The database boundary is the hard part (be honest)

Here's the uncomfortable truth the brief's mix of "SQL Server + stored procs + could-become-services"
runs into: **a shared database is the thing that most often makes splitting expensive.** If HR or
Accounting ever `JOIN`s to `dbo.[Order]`, you have a hidden coupling that no amount of clean C# can
fix — splitting the service means breaking their query.

**Discipline to keep now, to keep the option open:**

- Each module owns its tables. Cross-module reads go through the owning module's *queries/API*, not
  direct SQL joins.
- No foreign keys *across* module boundaries (inside a module, use them freely). A cross-module
  reference is an id you resolve via the other module — not an FK you join on.
- Resist "just this one little join." It's the single most common way modular monoliths quietly fuse
  into an un-splittable big ball of mud.

> ⚖️ **This is a real cost you pay now** (some convenient joins become API calls or duplicated data) for
> **an option you may never use.** Be honest with yourself: if you're *certain* these will never be
> separate services, the discipline may not be worth it, and a shared schema is simpler. The brief says
> assume they *might*, so we keep the boundary — but it's a genuine cost, not free architecture.

## Step 4 — Cross-module communication (when it becomes real)

**Within a module:** direct in-process calls and in-process domain events (Phase 7) are perfectly fine.

**Across modules** (now or after a split):

- **Commands/queries across modules** → go through an explicit contract (an interface now; HTTP/gRPC
  after a split). *Never* let module B build module A's objects or call its repository.
- **Reactions across modules** (e.g. Accounting reacts to `OrderBooked`) → use a **message /
  integration event**, *not* the in-process domain event. An integration event is a *published
  contract* (versioned, serializable), which is different from an internal domain event (which you can
  change freely). Conflating them couples modules to each other's internals.
- **Reliable** cross-module reactions need a **queue / outbox**, because (Phase 7) post-commit
  in-process dispatch is best-effort. The standard tool is the **transactional outbox** pattern: write
  the event to an outbox table *in the same transaction* as the state change, then a separate relay
  publishes it. Note it as the next thing to learn — **don't build it unless you actually split.**

## Step 5 — What the current design already got right

- **Aggregates as consistency boundaries** map cleanly onto service boundaries — a service owns whole
  aggregates.
- **No generic repository / no shared service layer** means there are no god-objects spanning modules
  to untangle.
- **Message-based call sites** (`ISender`) make swapping in-process for network transport localized.
- **Stored procs per module** keep data access owned — *as long as* you honor the no-cross-module-joins
  rule.

## Step 6 — What you would deliberately NOT do

- **Don't split now.** There's no scaling, team-autonomy, or deployment pressure to justify the
  operational cost (network failures, distributed debugging, cross-service data consistency).
- **Don't add a message broker "to be ready."** Add it when a real cross-module reliable reaction
  exists.
- **Don't distribute for its own sake.** A well-bounded modular monolith is the *right* architecture
  for the vast majority of systems this size. "Could be split later" is the feature — not "is split
  now."

## What to produce in this phase

- A short `MODULES.md` documenting the three module-ownership rules and the no-cross-module-join rule.
- Optionally, a light tidy-up grouping the `Orders` folders consistently across projects to make the
  module seam obvious (no behavior change).
- The extraction checklist (Step 2) and the outbox/integration-event note (Step 4), written as **design
  docs, not implementations**.

**NOT now:** actually creating a second module/service; adding a message broker; implementing an
outbox. These are documented as *the path*, not built.

## Common mistakes to avoid

- **Premature extraction.** The most expensive mistake available here — distributed systems cost
  operational complexity you don't need yet.
- **Cross-module joins / FKs.** The silent coupling that turns a future split into a rewrite.
- **Treating domain events as integration events.** Internal events change freely; published contracts
  must be versioned. Keep them separate types.
- **Building the outbox/broker "just in case."** Add reliability machinery when a real requirement
  exists, not speculatively.

---

## ✅ Check Yourself

1. **What three ownership rules define a module today, and why does keeping them make extraction
   cheap?**
2. **Why is the shared database (not the C# code) the hard part of splitting out a service?**
3. **What's the difference between a domain event and an integration event, and why must they be
   separate types?**
4. **Why is "a modular monolith that *could* split" usually the right answer over "microservices
   now"?**
5. **Which specific earlier decision (Phase 2) makes changing the call transport localized?**

---

## 🎉 You've reached the end

If you've worked through all ten phases and can answer the check-yourself questions, come back to
[00-START-HERE.md](00-START-HERE.md) and try the five big questions at the bottom. If you can explain
those without notes, this project has done its job — you don't just have working software, you
understand *why* every piece is shaped the way it is.
