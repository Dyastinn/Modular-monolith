# Phase 9 — Modularity & the Path to Services

**Goal:** understand how the Order module could become its own deployable service later, what the
current design already got right for that, and what would have to change. This is the "each department
could become its own service" requirement, made concrete — without actually splitting anything now.
**Produces:** module-boundary conventions, an honest extraction checklist, and notes on cross-module
communication. Mostly design + light refactor, not a new service.
**Prereq:** Phases 1–8. The Order module is complete and tested.

> **Guiding principle:** you do not build microservices you don't need. This phase makes the *option*
> cheap, not mandatory. A modular monolith that could be split later beats a distributed system you
> can't justify. State that plainly to resist premature extraction.

---

## Step 1 — What "module" means here today

Right now a module is a **folder convention** inside shared projects: `Orders` under `Domain`,
`Application`, `Infrastructure`, and `Api`. That's deliberate — folder boundaries are free to move and
have zero deployment cost. The module is defined by three things:

1. Its **own aggregates** (`Order`, `Customer`, `Material`, `Inventory`) — no other module reaches
   into them.
2. Its **own slices** (its commands/queries).
3. Its **own tables/procs** — other modules don't query the Order module's tables directly.

If those three hold, extraction later is mechanical. If they've been violated (another module joins to
`dbo.[Order]`, or shares the `Order` aggregate), extraction becomes surgery. So the discipline *now* is
what buys the cheap option *later*.

## Step 2 — The extraction checklist (what changes to make it a service)

To promote `Orders` from a module to a standalone service:

1. **Move the module's projects/folders into their own solution/assembly** (`Orders.Domain`,
   `Orders.Application`, etc.). Because you never shared aggregates across modules, nothing else
   depends on these types.
2. **Give it its own database (or schema).** The Order tables/procs move with it. This is only painless
   if no other module was querying them directly — see Step 3.
3. **Replace in-process calls with a remote boundary** (HTTP/gRPC) for anything another module needs
   from Orders. Your `ISender.Send(command)` calls that crossed a (future) module boundary become
   network calls; in-module calls stay local.
4. **Replace in-process domain events with real messaging** for cross-module reactions (Step 4).
5. **Handle the data you used to join to.** Anything another module read from Order tables now needs an
   API call or a replicated read model.

The mediator helps here: because callers already speak in terms of *messages* (`Send(command)`), the
transport under `ISend` can change from in-process to remote with the call sites largely unchanged. That
is a genuine, concrete payoff of Phase 2 you can now point to — not the whole justification for the
mediator, but a real one.

## Step 3 — The database boundary is the hard part (be honest)

The brief mixes SQL Server + stored procedures + "modules could become services." The uncomfortable
truth: **a shared database is the thing that most often makes extraction expensive.** If HR or
Accounting ever `JOIN` to `dbo.[Order]`, you have a hidden coupling that no amount of clean C# fixes —
splitting the service means breaking their query.

**Discipline to keep now, so the option stays open:**

- Each module owns its tables. Cross-module reads go through the owning module's *queries/API*, not
  direct SQL joins.
- No foreign keys across module boundaries (within a module, use them freely). A cross-module reference
  is an id you resolve via the other module, not an FK you join on.
- Resist "just this one join" — it's the single most common way modular monoliths quietly fuse into a
  big ball of mud that can't be split.

This is a **cost you pay now** (some convenience joins become API calls or duplicated read data) for an
**option you may never exercise.** State that tradeoff to yourself honestly: if you're *sure* these will
never be separate services, the discipline may not be worth it and a shared schema is simpler. The brief
says assume they might, so we keep the boundary — but it's a real cost, not free architecture.

## Step 4 — Cross-module communication (when it's real)

Within a module: direct in-process calls and in-process domain events (Phase 7) are fine.

Across modules (now or after extraction):

- **Commands/queries across modules** → an explicit contract (an interface now; HTTP/gRPC after split).
  Never let module B construct module A's aggregate or call its repository.
- **Reactions across modules** (e.g. Accounting reacts to `OrderBooked`) → a **message/integration
  event**, not the in-process domain event. Integration events are a *published contract* (versioned,
  serializable), distinct from internal domain events (which can change freely). Conflating them couples
  modules to each other's internals.
- Reliable cross-module reactions need a **queue/outbox**, because (Phase 7) post-commit in-process
  dispatch is best-effort. The **transactional outbox** pattern (write the event to an outbox table in
  the same transaction as the state change, then a relay publishes it) is the standard way to make
  cross-module events reliable without distributed transactions. Note it as the next thing to learn;
  don't build it unless you actually split.

## Step 5 — What the current design already got right

- **Aggregates as consistency boundaries** map cleanly to service boundaries — a service owns whole
  aggregates.
- **No generic repository / no shared service layer** means no god-objects spanning modules to untangle.
- **Message-based call sites** (`ISender`) make swapping in-process for remote transport localized.
- **Stored procs per module** keep data access owned, *if* you honor the no-cross-module-joins rule.

## Step 6 — What you would NOT do

- Don't split now. There's no scaling, team-autonomy, or deployment pressure justifying the operational
  cost (network failures, distributed debugging, data consistency across services).
- Don't add a message broker "to be ready." Add it when a real cross-module reliable reaction exists.
- Don't distribute for its own sake. A well-bounded modular monolith is the correct architecture for the
  vast majority of systems this size, and "could be split later" is the feature — not "is split now."

## What Sonnet should produce

- A short `MODULES.md` documenting the three module-ownership rules and the no-cross-module-join rule.
- Optionally, a light refactor grouping `Orders` folders consistently across projects to make the module
  seam obvious (no behavior change).
- A written extraction checklist (Step 2) and the outbox/integration-event note (Step 4) as design docs,
  not implementations.

**NOT in this phase:** actually creating a second module/service; adding a message broker; implementing
an outbox. These are documented as the path, not built.

## Pitfalls

- **Premature extraction.** The most expensive mistake available here; distributed systems cost
  operational complexity you don't need yet.
- **Cross-module joins / FKs.** The silent coupling that makes future extraction a rewrite.
- **Treating domain events as integration events.** Internal events change freely; published contracts
  must be versioned. Keep them separate types.
- **Building the outbox/broker "just in case."** Add reliability machinery when a real requirement
  exists, not speculatively.

---

## Checkpoint — you understand this phase when you can answer:

1. What three ownership rules define a module today, and why does keeping them make extraction cheap?
2. Why is the shared database (not the C# code) the hard part of splitting a service?
3. What's the difference between a domain event and an integration event, and why must they be separate?
4. Why is "a modular monolith that *could* split" usually the right answer over "microservices now"?
5. Which specific earlier decision (Phase 2) makes changing the call transport localized?
