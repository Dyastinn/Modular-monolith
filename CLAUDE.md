# CLAUDE.md — Guardrails for This Repository

> **Read this before editing anything.** This repo is a **teaching curriculum**, not a normal app. Its
> value is pedagogical consistency: one architecture, taught three ways, with every decision deliberate.
> The failure mode here is **derailing** — "helpfully" changing a convention, adding a banned technology,
> refactoring away an intentional simplification, or introducing an API the reader hasn't been shown. This
> file exists to prevent that. When a request conflicts with a rule here, **stop and flag the conflict**
> instead of silently breaking the course.

---

## 1. What this repository is

A course that teaches how to build a **Sales Order module** for a fictional ERP as a **Modular Monolith
with Vertical Slice Architecture (VSA)**. The deliverable is mostly **Markdown lessons** (plus `Erp.slnx`
and a `src/` skeleton). Three companion folders teach the *same* architecture with **three distinct jobs** —
this framing is deliberate; preserve it in any edit:

| Folder | Job (its purpose — keep this) | Structure |
|---|---|---|
| `plan-beginner-friendly/` | **Understand it.** Concept-first, plain-English: what problem each pattern solves and how the pieces fit. Analogies, answer keys, a small teaching slice. (Also WebForms-migrant friendly.) | Phases 0–9 + `00a-FOUNDATIONS.md`, `GLOSSARY.md`, `LEARNING-RESOURCES.md` |
| `plan-business-tutorial/` | **Build it for real.** Hands-on, production-*shaped* implementation: JWT, roles, credit limits, concurrency — the reader does it themselves. | Phases 0–13 |
| `plan-Opus/` | **Let AI build it.** A terse, unambiguous, AI-ready spec: hand a phase to a coding agent to implement exactly, so the learner sees it come together and reviews real code against the plan. | Phases 0–9 |

**Phase numbers line up across folders.** A change to a concept in one folder usually needs a matching
look at the sibling folders. Respect each folder's *job*: don't turn the beginner concepts into a terse
spec, don't strip the "why" out of it to match Opus, and keep Opus tight and implementable (no meandering
prose). `README.md` is the front door; keep it in sync with any structural change.

---

## 2. The locked stack — never swap, never "modernize" away

.NET 10 · C# · **Modular Monolith + VSA** · **Carter** over ASP.NET minimal APIs · **Dapper + stored
procedures** (NO ORM) · **hand-rolled mediator** (business tutorial also *compares* the free
`martinothamar/Mediator`) · **CQRS-lite** · **FluentValidation** · **SQL Server**, schema-per-module ·
**explicit SQL transactions in handlers** · **JWT bearer + role auth** (business tutorial).

### Banned technologies — do NOT introduce them into lessons or code
Clean Architecture's four-layer project split · **EF Core / any ORM** · Docker · Redis · RabbitMQ ·
MassTransit · any message broker · microservices · distributed caching · a generic `Repository<T>`.

The **only** place these may appear is a lesson's explicit *"when you'd add this later, and why not now"*
discussion (e.g. beginner Phase 9, business Phase 13). Never as part of the built solution.

> **MediatR** specifically is avoided (it moved to a paid license for larger orgs in 2025). Don't
> reintroduce it. The hand-rolled mediator is the lesson; `martinothamar/Mediator` is the sanctioned free
> alternative the business tutorial compares.

---

## 3. The non-negotiable architecture rules

1. **Modules are islands.** `Sales` never references `Inventory` (or vice-versa). Cross-module work goes
   through a **published contract in `Shared`** (e.g. `IStockReservation`) or an **integration event** —
   never a direct `using` or a cross-schema SQL `JOIN`/FK. The absent project reference *is* the wall.
2. **Keep the Domain pure.** Inside a module, `Features/` and `Infrastructure/` depend on `Domain/`;
   `Domain/` depends only on `Shared.BuildingBlocks`. No SQL, web, Carter, Dapper, or `HttpContext` in the
   domain. Ever.
3. **Where each kind of logic lives** (this "split" is the entire skill the course teaches — keep it sacred):

   | Concern | Home |
   |---|---|
   | "Is the request well-formed?" (shape: positive ids, ≥1 line) | **Validator** (FluentValidation, in the handler file) |
   | "Is this operation allowed?" (blocked customer, credit limit, status transition, approval threshold) | **Domain** — a method on the entity/value object it's about |
   | "Load → do → save" coordination | **Handler** (no business `if`s) |
   | Safety when two people act at once (reserve stock, lost-update guard) | **SQL** — one atomic statement / stored proc |
   | One module needs another (must be same transaction) | **Synchronous contract** in `Shared` |
   | One module reacts afterward (best-effort) | **Integration event** (after commit) |
   | "This endpoint is role-X only, full stop" | **Endpoint** (`RequireRole`) |
   | Auth that depends on the data ("your own customers only") | **Handler/domain** via `ICurrentUser` |

   A business `if` in a handler, or a business rule in a validator/stored proc, is a **defect** in this
   course. So is a rule that needs a database to unit-test.

---

## 4. Conventions to preserve when writing or editing code

- **Two files per feature:** `XxxHandler.cs` (Command/Query + Result/DTOs + Validator + Handler) and
  `XxxEndpoint.cs` (Request/Response + Carter `ICarterModule`). **Never** split into technical-layer folders
  (`Commands/`, `Handlers/`, `Validators/`, `Endpoints/`) — that's the anti-pattern VSA rejects.
- **Handlers use the marker interfaces** `ICommandHandler<,>` / `IQueryHandler<,>` (defined in beginner
  Phase 4 and business Phase 2), not raw `IRequestHandler<,>`. The demo `PingHandler` is the one exception.
- **Strongly-typed IDs** (`readonly record struct SalesOrderId(long Value)`), never bare `long` for an id
  in the domain.
- **Rich domain:** private setters, `Create()` factory + private constructor for rehydration, computed
  `Total` (never a settable field), guarded `Transition`, value objects (`Money`, `Quantity`) that reject
  invalid values in their constructor.
- **Repositories are aggregate-specific** (`ISalesOrderRepository`), never a generic `Repository<T>`.
- **Stored procs are schema-qualified** in both definition *and* every C# call
  (`"sales.usp_SalesOrder_Insert"`, never bare `"usp_..."`).
- **Transactions are explicit and visible in the handler:** `await using var tx = await uow.BeginAsync(ct)`;
  writes run on that transaction; **throw = rollback** (no explicit `Rollback` call). Read-only validation
  happens *before* `BeginAsync`.
- **Domain events dispatch after commit** (best-effort).
- **Role strings are the literal values** `SalesRep`, `SalesManager`, `Admin` — never `"Manager"` or other
  paraphrases (a typo silently breaks an auth check).
- **Every type or method shown must be defined somewhere the reader has already met** (or defined right
  there). No phantom APIs. If you add an interface/method, define it *and* register it in DI.
- **File references in prose use Markdown links** (`[file.md](path)`), not backticks or bare paths.

---

## 5. Intentional cross-course differences — do NOT "reconcile" these

These look like inconsistencies but are **deliberate** and explained in-text. Do not change one to match
the other; each course is internally consistent on purpose.

| Topic | beginner-friendly | business-tutorial |
|---|---|---|
| Status enum | `Draft, Submitted, Cancelled, Completed` | `Draft, PendingApproval, Approved, Cancelled, Rejected` |
| Approval | folded into `Submit(bool approvalGranted)` — a single write path, **explicitly unenforced** (see the ⚠️ in Phase 3/5) | separate manager-role-gated `POST /approve` |
| Transaction handle property | `tx.Inner` | `tx.Transaction` |
| Event dispatch names | `INotification` / `INotificationHandler` / `IPublisher` | `IDomainEvent` / `IDomainEventHandler` / `IDomainEventDispatcher` |
| Pricing | `IPricingService` (a **read port**, i.e. data access — *not* a DDD "domain service") | item price loaded directly in the handler |
| Fulfillment / stock deduct | `CompleteSalesOrder` deducts stock (`DeductAsync` on the contract) | deferred to a Phase 13 extension |

If asked to unify them, confirm intent first — the reconciliation *tables* already added to both courses
are the sanctioned way to bridge the vocabulary without erasing the difference.

---

## 6. Intentional simplifications — flagged, not bugs

These are known, documented limitations left as learning exercises. **Do not silently "fix" them in the
base lessons**; they are called out with ⚠️ notes and listed on the business Phase 13 checklist.

- **Credit limit counts only the committed balance**, not in-flight draft/pending orders (business Phase 5
  §7).
- **`OnHand` never decreases** in the business base build (no shipment step; Phase 13 extension #3).
- **`RequiresApproval` is a surfaced flag**, not a workflow branch, in the business tutorial (auto-approve
  is Phase 13 extension #1).
- **No rate limiting or HTTPS enforcement** in the base build (Phase 13 checklist). *(Refresh tokens are now
  built into the base build — business Phase 4 §9: short access tokens + rotating refresh tokens with reuse
  detection. No longer a deferred simplification.)*

If a change genuinely *implements* one of these, do it as the documented extension and update the
checklist — don't graft it into the base flow where it would muddy a first read.

---

## 7. Pedagogical style to keep (this is the course's whole voice)

- **Pain-first.** Show the naive/broken version and *why it hurts* before presenting the fix (mediator,
  anemic-vs-rich domain, the concurrency race). Never lead with the solution.
- **Consistent analogies** — don't invent competing ones: mediator = *post office*; atomicity = *turnstile*;
  JWT = *festival wristband*; modules = *islands / locked floors*; VSA = *taco drawer*; value object = *any
  $10 bill*.
- **Every phase keeps its shape:** "In one sentence," "What you'll build," "Before you start," worked
  request trace, "Common mistakes," "Check Yourself," and (beginner) an **Answer Key**, or (business) the
  five fixed questions.
- **Callout conventions:** 💡 analogy · ❓ "you might be wondering" · ⚠️ honest caveat/limitation · 🔑 key
  point · 🚫 out of scope · 🧭 "from WebForms" translation · 📎 link to the slow/expert version · ✅/❌ best
  practice. Reuse these; don't invent new emoji semantics.
- **Cross-links:** beginner/business phases link to the matching `plan-Opus` and to each other ("slow
  version of this idea"). Keep links valid when you move or rename anything.
- **Be honest about costs.** Every pattern's downsides are named (mediator indirection, reflection cost,
  the saga/`IDbTransaction`-can't-cross-a-network caveat). Preserve that candor; don't oversell.

---

## 8. Pre-edit checklist (run this before changing a lesson)

1. **Does this belong in the locked stack / respect the banned list?** If it adds banned tech to the built
   solution, stop.
2. **Does it keep the "where logic lives" split intact?** No business rules leaking into validators,
   handlers, or stored procs.
3. **Is every type/method I introduce defined and registered, and already familiar to the reader** at this
   point in the sequence (no forward-referencing an undefined API)?
4. **Did I check the sibling folders** (beginner ⇄ business ⇄ Opus) for the same concept, and either stay
   consistent or preserve a *documented* intentional difference (§5)?
5. **Did I preserve phase structure, analogies, callout conventions, and cross-links?**
6. **Are `.NET 10` / API claims current?** (e.g. `Microsoft.AspNetCore.OpenApi` is the .NET 9+ default;
   Swashbuckle is now third-party; `IExceptionHandler`, `Rfc2898DeriveBytes.Pbkdf2`, `.slnx` all exist.)
7. **If I touched the solution layout, feature convention, or the rules — did I update `README.md`?**

When in doubt, **ask or flag** rather than guess. A confused-but-consistent lesson is recoverable; a
silently derailed one teaches the wrong thing.
