# Phase 0 — Orientation & Mental Model Shift

**Goal:** rewire how you think about "where code goes" before any code exists.
**Produces:** nothing. This is reading.
**Prereq:** read `00-README-overview.md`.

---

## 1. The WebForms model you're leaving

In WebForms, a page *is* the unit of everything:

```
OrderEntry.aspx        <- markup
OrderEntry.aspx.vb     <- code-behind: UI events, validation, business rules,
                          SQL calls, transactions — all in Button_Click
```

Everything for that screen in one file. One genuine virtue we *keep*: **cohesion** — related code lives
together. The problems the new architecture attacks:

- **UI and business logic fused.** Can't test "can this order be submitted?" without a page and a DB.
- **Rules duplicated across pages** and drifting.
- **No boundaries.** Any page can hit any table.

## 2. What VSA keeps and the modular monolith adds

**Vertical Slice Architecture keeps WebForms' cohesion but drops the UI coupling.** Instead of "everything
for a *screen* in one file," it's "everything for a *use case* in one folder" — as **two files**:

```
Modules/Sales/Features/CreateSalesOrder/
    CreateSalesOrderHandler.cs    <- Command + Result + Validator + Handler (the behavior)
    CreateSalesOrderEndpoint.cs   <- Request + Response + Carter endpoint (the web edge)
```

**The modular monolith adds two rules** (replacing Clean Architecture's four concentric layers):

1. **Modules are islands.** The top-level split is by capability (`Sales`, `Inventory`), one project each.
   A module never references another's internals; cross-module contact goes through a published contract in
   `Shared` or an integration event.
2. **Keep the Domain pure.** Inside a module, `Features/` and `Infrastructure/` depend on `Domain/`;
   `Domain/` depends only on `Shared.BuildingBlocks`. That's what makes rules unit-testable in
   milliseconds — the one good idea kept from Clean Architecture, minus the four-project ceremony.

```
Sales ──► Shared ◄── Inventory        Features/ ──► Domain/ ◄── Infrastructure/
  └──── NOT allowed ────┘              (pure: depends only on Shared.BuildingBlocks)
```

## 3. The mental substitutions

| WebForms thought | New home | Why it moved |
|---|---|---|
| `Button_Click` orchestration | **Handler** (feature file) | Orchestration isn't UI; separating it makes it testable. |
| Input validation in the page | **Validator** (same file as handler) | Use-case concern, checked before the domain runs. |
| "Can this order be submitted?" | **Domain entity method** | Business rule about the *thing*, belongs on `SalesOrder`. |
| Inline `SqlCommand` + connection | **Repository / query handler** (module `Infrastructure/`) | Persistence detail behind an interface. |
| Transaction in code-behind | **Command handler** (explicit) | Boundary is a use-case decision; kept visible. |
| Copy-pasted rule across pages | **One place** (domain) | Single source of truth kills drift. |
| "Any page hits any table" | **Module walls + schema-per-module** | Sales can't touch `inventory.*`; it uses a contract. |

## 4. Concepts, one line each

- **Module** — a capability's self-contained project (`Sales`), owning its domain, features, and schema.
- **Command / Query** — a request that *changes* / *reads* state.
- **Handler** — executes one command/query; orchestrates, delegates rules to the domain.
- **Mediator** — `Send(command)` finds and runs the right handler (Phase 2), in `Shared.Messaging`.
- **Pipeline behavior** — cross-cutting wrap (logging, validation) over every handler (Phase 2).
- **Carter** — thin minimal-API layer; each feature declares its own `ICarterModule` endpoint.
- **Entity / Value object / Aggregate / Aggregate root** — DDD building blocks; base types in
  `Shared.BuildingBlocks`, the `SalesOrder` aggregate in `Sales/Domain`.
- **Domain event** — a record that something happened (`SalesOrderSubmitted`) (Phases 3, 7).
- **Contract / integration event** — the two sanctioned cross-module channels (Phase 5, 7).
- **Repository** — persists/loads *one aggregate*. No generic repository.
- **DTO** — flat data shape for crossing a boundary.

## 5. A worked "where does it go?" example

*"An order over $50,000 needs manager approval before it can be submitted."*

- *"Is the input a valid request (order id present)?"* → **Validator**.
- *"Is $50,000 the threshold, and does this order exceed it?"* → **Domain** (`SalesOrder.Submit()` refuses
  if approval is required and absent).
- *"Fetch the order, run `Submit()`, persist, commit."* → **Handler** orchestration.
- *"Store the new status atomically without clobbering a concurrent change."* → **Stored procedure** with
  an optimistic version check (atomicity → SQL, Tension A.1).

No single place does all of it, and none is in a "page." That decomposition *is* the skill.

---

## Checkpoint — you understand this phase when you can answer:

1. What does VSA keep from WebForms, and what does it drop?
2. State the two rules of the modular monolith and why each exists.
3. For the "$50,000 approval" rule, name which part goes to the validator, domain, handler, and stored
   procedure — and why.
4. Why is a copy-pasted "customer must be active" check a problem, and where does it live now?
5. Why two files per feature, not four or one?
