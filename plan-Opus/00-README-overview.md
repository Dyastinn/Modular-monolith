# 00 — Overview & Architecture Decisions

This is the master document. Read it once, fully, before starting Phase 0. It holds every cross-cutting
decision so the phase files don't repeat themselves. Phase files *reference* decisions made here rather
than re-arguing them.

> **How to read this set:** one file per phase. Each phase file is self-contained enough to hand to an
> implementer as "build exactly this, nothing more." Review against the phase's **Checkpoint** before
> moving on.

---

## 1. What we are actually building

A **Sales** module for a fictional enterprise ERP — plus a second **Inventory** module — built as a
**modular monolith** using **Vertical Slice Architecture** with a rich domain. The two-module setup is
deliberate: it forces the cross-module boundary (Sales reserves stock owned by Inventory) that a
single-module example can't teach.

The domain has real rules (not CRUD):

- Create a sales order (draft) with line items
- Price it (line prices, totals)
- Validate the customer (credit hold, active status)
- Reserve inventory atomically, deduct on completion, release on cancel — **across the module wall**
- Submit an order (draft → submitted) with an approval rule for large orders
- Cancel an order (releases reservations), with rules about *which* states can cancel
- Concurrency + transactions around the money-and-stock operations

That list makes "where does this logic go?" have a *non-obvious* answer. If everything were CRUD, the
architecture would be pointless ceremony.

---

## 2. Verified facts (checked, with dates)

- **.NET 10** — LTS, current baseline. *(fact)*
- **MediatR licensing** — dual-licensed since **2 Jul 2025**; free under $5M revenue, paid above. We
  hand-roll anyway because **building it teaches the pattern better than importing it**. *(fact + opinion)*
- **Free MediatR-shaped library** — `martinothamar/Mediator` (source-generated, MIT — verify LICENSE).
  Noted as the drop-in if you stop wanting to maintain your own. *(fact)*
- **Carter** — thin library over ASP.NET minimal APIs (`ICarterModule`, `app.MapCarter()`), MIT. Used so
  each feature owns its endpoint at the web edge. Unlike the mediator, endpoint routing isn't a teaching
  target — using the library is the production-quality call. *(fact + opinion)*
- **FluentValidation** — MIT, idiomatic input validation. Same reasoning as Carter. *(fact)*

---

## 3. The tensions in the brief (read this carefully)

### Tension A — Rich domain **vs.** "business rules in SQL" *(most important)*

Putting order rules in **both** stored procedures **and** domain entities gives *split-brain logic* — two
sources of truth that drift. Resolution — ownership split, no duplication:

| Concern | Owner | Why |
|---|---|---|
| Invariants & decisions ("can this order be cancelled?", "is this transition legal?", "qty > 0?") | **C# domain** | Pure, unit-testable with zero DB. |
| Persistence, mapping, set-based reads | **Stored procedures / Dapper** | What SQL is good at. |
| **Atomic concurrency guards** ("reserve only if enough remains", version check) | **Stored procedures** | Must be atomic at the DB or you get races. See A.1. |
| Data-integrity constraints (FKs, NOT NULL, CHECK) | **SQL schema** | Enforced regardless of caller. |

**Rule of thumb:** *policy* lives in the domain; *integrity and atomicity* live in SQL. When a rule looks
like both, ask "does concurrency make the in-memory check unsafe?" Yes → SQL. No → domain.

#### Tension A.1 — the one legitimate overlap

"Reserve only if `Available >= Requested`" feels like a domain rule, but two concurrent in-memory checks
both pass and oversell. The **final** guard is a single atomic SQL statement (owned by Inventory):

```sql
UPDATE inventory.Stock
SET Reserved = Reserved + @Qty
WHERE ProductId = @ProductId
  AND (OnHand - Reserved) >= @Qty;   -- 0 rows affected if not enough
```

The domain *expresses* the rule (fast feedback, testing); SQL *enforces* it under concurrency. Phase 5
dwells on it.

### Tension B — Generic `TId` **vs.** "avoid unnecessary abstraction"

A global generic `TId` threaded through every entity/handler/repository/mapping is **viral**. Resolution —
strongly-typed IDs per aggregate:

```csharp
public readonly record struct SalesOrderId(long Value);
public readonly record struct CustomerId(long Value);
public readonly record struct ProductId(long Value);
```

- **Type safety** — can't pass a `CustomerId` where a `SalesOrderId` is expected.
- **Swap the backing type** in one line per aggregate + one Dapper `TypeHandler` (~10 lines each). No viral
  generic. *(direct override of "generic TId"; decide before Phase 3.)*

### Tension C — VSA + Modular Monolith + DDD (the project's spine)

These compose *well* — better than "VSA + Clean Architecture," which fights itself by splitting one feature
across four technical-layer projects.

- **Modular monolith** — top-level split by **business capability**: one project per module (`Sales`,
  `Inventory`), plus a `Shared` kernel and an `Api` host.
- **VSA** — inside a module, organize by **feature**: `Features/CreateSalesOrder/`, each a **two-file**
  slice (Handler file = Command + Result + Validator + Handler; Endpoint file = Request + Response + Carter
  endpoint).
- **DDD** — each module has a `Domain/` folder (its aggregates, value objects, events) that depends only on
  `Shared.BuildingBlocks`.

The resolution replaces Clean Architecture's four concentric layers with **two rules**:

1. **Modules are islands** — a module never references another's internals (compiler-enforced: no project
   reference). Cross-module contact is a published contract in `Shared` (`IStockReservation`) or an
   integration event.
2. **Keep the Domain pure** — inside a module, `Features/` and `Infrastructure/` depend on `Domain/`;
   `Domain/` depends only on shared building blocks. Same testability payoff as Clean Architecture's inner
   circle, via folder discipline, not four projects.

Phase 1 draws the exact tree and defends every folder.

### Tension D — "CQRS" here is CQRS-lite

Separate command handlers (through the domain, write) and query handlers (bypass domain, Dapper → DTO), on
the **same** database. **Not** built: separate read store, projections, event sourcing, eventual
consistency. Phase 6 names what full CQRS would add.

---

## 4. Technology decisions (summary)

| Decision | Choice | Rationale |
|---|---|---|
| Runtime | .NET 10 (LTS) | Current, supported. |
| Architecture | Modular monolith + VSA | Split by capability, then feature; islands + pure domain. |
| Data access | Dapper + stored procedures | Explicit SQL, no ORM magic. |
| ORM | **None** (no EF) | Persistence stays explicit; no change-tracker crutch. |
| Web edge | **Carter** (ICarterModule) | Each feature owns its endpoint; VSA to the web edge. |
| Mediator | **Hand-rolled** (MediatR-shaped API), in `Shared.Messaging` | Free, teaches the pattern. |
| IDs | Strongly-typed per aggregate | See Tension B. |
| Validation | FluentValidation (MIT) | Free, idiomatic; input-shape only. |
| Repository | **Per-aggregate**, no generic repository | Generic repos leak persistence, encourage anemic domains. |
| Cross-module | Synchronous **contract** in Shared, or **integration event** | Never a direct reference/join across modules. |
| Schema | One DB **schema per module** (`sales`, `inventory`) | Data boundary echoes the code boundary. |
| Transactions | **Explicit** in command handlers | Boundary visible while learning. |

---

## 5. Phase map

| Phase | File | Produces | Core lesson |
|---|---|---|---|
| 0 | `01-phase-0-orientation.md` | Nothing (reading) | WebForms → VSA/module mental model |
| 1 | `02-phase-1-skeleton.md` | Solution, Shared kernel, Sales module, Carter host, one trivial slice | Where code lives; walking skeleton |
| 2 | `03-phase-2-mediator.md` | Hand-rolled mediator + behaviors in `Shared.Messaging` | What the mediator solves |
| 3 | `04-phase-3-domain.md` | `SalesOrder` aggregate, value objects, IDs, events | Rich vs anemic; pure domain |
| 4 | `05-phase-4-create-order.md` | `CreateSalesOrder` slice (two files, Dapper + proc + tx) | Anatomy of a command slice |
| 5 | `06-phase-5-inventory-concurrency.md` | **Inventory module**, `IStockReservation` contract, `SubmitSalesOrder`, concurrency | Module boundary; atomicity vs policy |
| 6 | `07-phase-6-queries.md` | `GetSalesOrder`, `SearchSalesOrders` read slices | Why queries bypass the domain |
| 7 | `08-phase-7-remaining-slices.md` | `CancelSalesOrder`, `UpdateSalesOrder`, `CompleteSalesOrder`, events | State transitions; integration events |
| 8 | `09-phase-8-testing.md` | Unit, integration, e2e tests | What each test level is for |
| 9 | `10-phase-9-modularity.md` | Extraction path for a module | How a module becomes a service |

---

## 6. How to drive this (practical)

1. **One phase per session.** "Implement exactly this phase. Nothing from later phases. Stop at the
   Checkpoint and summarize what you built and why."
2. **Review against the Checkpoint** before continuing.
3. **Keep a decisions log** for any override.
4. **Resist scope creep** — Clean Architecture layers, EF, MediatR, a generic repository, a cross-module
   join all signal drift.
5. **Build order is dependency order.**

---

## 7. Success criteria

You're done when you can, without notes, explain:

- Why a command slice and a query slice are shaped differently.
- Which business operation has its rule in the domain, which in SQL, and *why*.
- What the mediator gave you over a direct call — and what it cost.
- Why Sales may not reference Inventory, and how they communicate instead.
- What would have to change to split Inventory into its own service.

If you can explain those five, the architecture has done its teaching job.
