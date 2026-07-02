# Phase 0 — Orientation & Mental Model Shift

**Goal:** rewire how you think about "where code goes" before any code exists.
**Produces:** nothing. This is reading. Do not skip it — every later phase assumes this model.
**Prereq:** read `00-README-overview.md`.

---

## 1. The WebForms model you're leaving

In WebForms, a page *is* the unit of everything:

```
OrderEntry.aspx        <- markup
OrderEntry.aspx.vb     <- code-behind: UI events, validation, business rules,
                          SQL calls, transactions — all in Button_Click
```

`Button_Click` typically does: read controls → validate → build SQL → open connection →
run it in a transaction → update controls → maybe redirect. Everything for that screen is
in one file. That has one genuine virtue we will *keep*: **cohesion** — related code lives together.

Its problems (the ones the new architecture attacks):

- **UI and business logic are fused.** You can't test "can this order be booked?" without a page,
  a request, and a database.
- **Rules are duplicated across pages.** The same "customer must be active" check gets copy-pasted
  into every `.aspx.vb` that touches customers, and they drift.
- **No boundaries.** Any page can do anything to any table. Change one table's shape and you hunt
  through dozens of code-behinds.

## 2. The insight VSA keeps and Clean Architecture adds

**Vertical Slice Architecture keeps WebForms' cohesion but drops the UI coupling.** Instead of
"everything for a *screen* in one file," it's "everything for a *use case* in one folder":

```
Features/Orders/CreateOrder/
    CreateOrderCommand.cs      <- the request (inputs)
    CreateOrderValidator.cs    <- input validation
    CreateOrderHandler.cs      <- orchestration for this one use case
    CreateOrderEndpoint.cs     <- the HTTP entry point
```

A "slice" is a use case, top to bottom. When a bug report says "creating an order is broken,"
you open **one folder**, not five layers.

**Clean Architecture adds the dependency rule** for the things slices *share*: the domain model
and infrastructure. The rule is a single arrow direction:

```
Api  ->  Application (slices)  ->  Domain
                 \                    ^
                  \                   |  (Domain depends on NOTHING)
                   ->  Infrastructure -/  (implements interfaces Domain/Application declare)
```

Domain (your `Order` aggregate) knows nothing about SQL, HTTP, or Dapper. That's what makes it
unit-testable in milliseconds. Infrastructure depends *inward* on interfaces the inner layers define.

## 3. The mental substitutions

| WebForms thought | New home | Why it moved |
|---|---|---|
| `Button_Click` orchestration | **Handler** (Application slice) | Orchestration isn't UI; separating it makes it testable and reusable. |
| Input validation in the page | **Validator** (Application slice) | "Is the input well-formed?" is a use-case concern, checked before the domain runs. |
| "Can this order be booked?" | **Domain entity method** | That's a business rule about the *thing*, not the screen. It belongs to `Order`. |
| Inline `SqlCommand` + connection | **Repository / query handler** (Infrastructure) | Persistence detail; hidden behind an interface so the domain stays pure. |
| Transaction in code-behind | **Command handler** (explicit) | The transaction boundary is a use-case decision; we keep it visible. |
| Copy-pasted rule across pages | **One place** (domain or a domain service) | Single source of truth kills drift. |

## 4. The concepts, one line each (glossary)

You'll meet these across the phases. Rough definitions now; each gets its own "why" later.

- **Command** — a request that *changes* state (`CreateOrder`, `BookOrder`). Returns little or nothing.
- **Query** — a request that *reads* state (`GetOrder`). Never changes anything.
- **Handler** — the code that executes one command or query. Orchestrates; delegates rules to the domain.
- **Mediator** — a dispatcher: `Send(command)` finds and runs the right handler, so callers don't wire handlers by hand. (Phase 2.)
- **Pipeline behavior** — cross-cutting code (logging, validation, transactions) that wraps every handler. (Phase 2.)
- **Entity** — a domain object with identity and a lifecycle (`Order`, `Customer`).
- **Value object** — a domain object defined by its values, not identity (`Money`, `Quantity`). Immutable.
- **Aggregate** — a cluster of entities/value objects treated as one consistency unit.
- **Aggregate root** — the single entry point to an aggregate; outsiders only touch the root (`Order`,
  not its `OrderLine`s directly). Enforces the aggregate's invariants.
- **Domain event** — a record that something business-meaningful happened (`OrderBooked`). (Phases 3, 7.)
- **Domain service** — logic that spans multiple aggregates and has no natural home on one entity. Rare. (Phase 5.)
- **Repository** — persists/loads *one aggregate*. No generic repository here (see overview). (Phase 4.)
- **DTO** — a flat data shape for crossing a boundary (query result, API response). No behavior.

## 5. A worked "where does it go?" example

Business statement: *"An order over $50,000 needs manager approval before it can be booked."*

Decompose it:

- *"Is the input a valid booking request (order id present, well-formed)?"* → **Validator**.
- *"Is $50,000 the threshold, and does this order exceed it?"* → **Domain** (`Order` knows its total;
  the threshold is a domain rule). The `Order.Book()` method refuses if approval is required and absent.
- *"Fetch the order, run `Book()`, persist, commit."* → **Handler** orchestration.
- *"Store the new status atomically without clobbering a concurrent change."* → **Stored procedure**
  with an optimistic version check (atomicity → SQL, per Tension A.1).

Notice no single layer does all of it, and none of it is in a "page." That decomposition *is* the skill
this project builds.

---

## Checkpoint — you understand this phase when you can answer:

1. What does VSA keep from the WebForms model, and what does it drop?
2. Which direction does the dependency arrow point, and why does the Domain depend on nothing?
3. For the "$50,000 approval" rule, name which part goes to the validator, the domain, the handler,
   and the stored procedure — and why each.
4. Why is a copy-pasted "customer must be active" check a problem, and where does it live now?

No code is produced in this phase. Proceed to Phase 1 only once these answers are comfortable.
