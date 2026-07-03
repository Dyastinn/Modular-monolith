# Phase 9 — Modularity & the Path to Services

**Goal:** you already have a modular monolith — two modules (`Sales`, `Inventory`) with compiler-enforced
walls, talking only through a contract and events. Understand what it takes to promote a module into its own
deployable service, and why you shouldn't yet.
**Produces:** module-boundary conventions, an extraction checklist, the outbox note. Design + docs, not a new
service.
**Prereq:** Phases 1–8.

> **Guiding principle:** you do not build microservices you don't need. This phase makes the *option* cheap,
> not mandatory.

---

## Step 1 — What a "module" already is (you built it)

A module is a **real project** (`Erp.Modules.Sales`, `Erp.Modules.Inventory`) whose walls the compiler
enforces — proven in Phase 5 when Sales had to reach Inventory through `IStockReservation` because it
*couldn't* reference it. It owns:

1. Its **aggregates** (`SalesOrder`; Inventory's `Stock`) — unreferenced by other modules.
2. Its **slices** + DI registration (`AddSalesModule`).
3. Its **schema/procs** (`sales.*`, `inventory.*`) — unqueried by other modules.

Two sanctioned cross-module doorways, both built: a **synchronous contract** in Shared (`IStockReservation`)
for same-transaction work, and an **integration event** for react-afterwards. Because these hold and are
compiler-enforced, extraction is mechanical, not surgery.

## Step 2 — Extraction checklist (promote Inventory to a service)

1. **Move its project to its own solution/repo.** Nothing depends on its internals (only the Shared
   contract).
2. **It already has its own schema** — tables/procs move painlessly (no other module joined them).
3. **Swap the contract implementation for a network client** behind the same `IStockReservation`. Sales
   unchanged.
4. **Replace in-process integration events with real messaging.**
5. **Handle cross-boundary consistency** — the submit→reserve that shared one transaction becomes a saga
   with compensation (reserve; release on failure). The real cost of the split, named.

The seams (`ISender`, `IStockReservation`) localize the transport change — call sites barely move.

## Step 3 — The database boundary is the hard part

A shared database is what most often makes extraction expensive. You avoided it: schema-per-module, no
cross-schema joins. Keep the discipline: cross-module reads go through the owning module (contract/query);
no cross-module FKs; resist "just one join into `inventory.*`." A real cost now (some joins become contract
calls) for an option you may never use — but the brief assumes modules might split, so keep the boundary.

## Step 4 — Cross-module communication at scale

- **Commands/queries across modules** → the contract (interface now; HTTP/gRPC after split). Never build
  another module's aggregate.
- **Reactions** → an **integration event** (published, versioned, serializable), *not* the internal domain
  event — keep them **separate types**, and give each module its own `*.Contracts` project once you have
  several (one shared `Contracts` folder is fine for two).
- **Reliable** cross-module reactions → a **transactional outbox** (write the event in the state-change
  transaction; a relay publishes it). Note it; build only on a real split.

## Step 5 — What the current design already got right

Two modules with compiler-enforced walls; per-module schemas with no cross-schema joins; aggregates as
consistency boundaries mapping to service boundaries; no generic repository / shared service layer;
message-based call sites + the reservation contract localizing transport changes.

## Step 6 — What you would NOT do

Don't split now (no scaling/autonomy/deploy pressure); don't add a broker "to be ready"; don't distribute for
its own sake. A well-bounded modular monolith is correct for most systems this size — "could split" is the
feature.

## What to produce

- `MODULES.md`: the three ownerships, the two doorways, the no-cross-schema-join rule.
- The extraction checklist and outbox note as design docs.

**NOT:** extracting a module; a broker; an outbox implementation.

## Pitfalls

- Premature extraction.
- Cross-schema joins / cross-module FKs.
- Treating domain events as integration events (separate types).
- Building the outbox/broker "just in case."

---

## Checkpoint

1. What three ownerships define a module, and how did Phase 5 prove the walls?
2. Why is the shared database the hard part of splitting?
3. Domain event vs. integration event — why separate types?
4. Why "could split" over "microservices now"?
5. Which two seams localize a transport change?
