# CLAUDE.md — Guardrails for This Repository

> **Read this before editing anything.** This repo is a **teaching curriculum**, not a normal app. Its
> value is pedagogical: one architecture, taught properly, with every decision deliberate and every step
> explained. The failure mode here is **derailing** — "helpfully" changing a convention, adding a banned
> technology, refactoring away an intentional simplification, skipping an explanation because it seems
> obvious, or introducing an API the reader hasn't been shown. This file exists to prevent that. When a
> request conflicts with a rule here, **stop and flag the conflict** instead of silently breaking the course.

---

## 1. What this repository is

A course that teaches how to build a **Sales Order module** for a fictional ERP as a **Modular Monolith
with Vertical Slice Architecture (VSA)**. The deliverable is mostly **Markdown lessons** (plus `Erp.slnx`
and a `src/` skeleton).

| Path | What it is |
|---|---|
| `plan-business-tutorial/` | **The course.** The only course. Phases 0–14, plus `00a-FOUNDATIONS.md`, `GLOSSARY.md`, `LEARNING-RESOURCES.md`. Hands-on, production-*shaped*: JWT, roles, credit limits, concurrency — the reader builds it themselves. |
| `course-validation/business/` | A complete, runnable reference implementation of the tutorial, plus `FINDINGS.md` (a validation log). Not teaching material; it's there to check the lessons against reality. |
| `README.md` | The front door. Keep it in sync with any structural change. |

**The tutorial is self-contained and it is the whole product.** There are no companion courses, no sibling
folders, no "see the other plan for the slow version." Everything the reader needs is in
`plan-business-tutorial/`. If a concept is needed, it gets explained *there*, at the point it's needed.

> There used to be `plan-beginner-friendly/` and `plan-Opus/` folders. They were **deleted on purpose** —
> nobody read them and they pulled effort away from the real course. **Do not recreate them**, do not add
> links to them, and do not reintroduce their vocabulary (`INotification`, `Submit(bool approvalGranted)`,
> `tx.Inner`, `IPricingService`, `IUnitOfWork`). If you find a surviving reference to either folder, that's
> a bug — remove it.

---

## 2. How this course teaches — progressive, complete, honest

This is the part most likely to get eroded, so it comes before the technical rules. **A lesson that is
technically correct but skips the "why" has failed.**

1. **Assume nothing about the reader.** Not that they know DDD, CQRS, dependency injection, `async`/`await`,
   what an aggregate is, why a `record struct` exists, or what a race condition looks like. If a phase uses
   an idea, that phase (or an earlier one) must have taught it. When in doubt, explain it.
2. **Progressive.** Each phase builds only on what earlier phases actually established. Never forward-
   reference an undefined API and never say "as you know." If Phase 8 needs something, Phase 8 teaches it or
   Phase 1–7 already did. **Each phase must end in a runnable, verifiable result** the reader can see working
   before moving on.
3. **No shortcuts, no hand-waving.** Every type and method shown is defined somewhere the reader has already
   met — or defined right there, in full, with its DI registration. No phantom APIs. No "the details don't
   matter here." No "left as an exercise" for anything on the critical path.
4. **Pain first, solution second.** Show the naive version and *why it hurts* before presenting the fix
   (the mediator, the anemic-vs-rich domain, the concurrency race). Never lead with the answer. The reader
   should feel the problem before they're handed the tool.
5. **Explain the reasoning, not just the mechanics.** "Put the rule in the domain" is worthless without "…
   because a rule in the validator can't be unit-tested with the entity it's about, and it silently stops
   being enforced the moment another slice writes the same data."
6. **Be honest about costs.** Every pattern's downsides get named — mediator indirection, reflection cost,
   the `IDbTransaction`-can't-cross-a-network caveat, best-effort event dispatch. Never oversell. If
   something is a simplification, say so with a ⚠️.

### Write like a person, not like an AI

The prose should read like a patient senior engineer explaining something at a whiteboard. Concretely:

- **Don't** open sections with "In today's fast-paced world," "Let's dive in," "It's important to note that,"
  "By leveraging," or "In conclusion."
- **Don't** pad with filler ("As we discussed," "Now that we've covered"), restate the heading as the first
  sentence, or end a section by summarizing what the reader just read.
- **Don't** write in symmetrical, listicle-shaped paragraphs where every bullet has the same length and
  rhythm. Vary sentence length. Let some points be one clause.
- **Don't** hedge everything ("this can potentially help improve"). Say what's true. Say what's a trade-off.
- **Do** use direct address ("you"), concrete numbers, and real examples with real names from this domain
  (`SalesOrder`, `SalesManager`, ₱ amounts).
- **Do** admit when something is awkward, when a convention is arbitrary, or when you're simplifying.
- **Do** keep the existing voice: 👨‍🏫 "from your senior," plainspoken, occasionally wry, never breathless.

### Fixed structure and callouts

- **Every phase keeps its shape:** an opening frame ("What you'll build" / "In one sentence"), "Why it's
  designed this way," a worked request trace, "Why each component exists," "Common mistakes & best
  practices," a **Checkpoint** (something runnable), and **"Check Yourself"** questions at the end. Every
  phase has the Checkpoint and Check Yourself; don't drop them.
- **Consistent analogies** — don't invent competing ones: mediator = *post office*; atomicity = *turnstile*;
  JWT = *festival wristband*; modules = *islands / locked floors*; VSA = *taco drawer*; value object = *any
  $10 bill is as good as any other* (per `GLOSSARY.md` — money in the lessons is ₱, but this analogy is $).
- **Callout conventions:** 💡 analogy · ❓ "you might be wondering" · ⚠️ honest caveat/limitation · 🔑 key
  point · 🚫 out of scope · 🧭 "from WebForms" translation · ✅/❌ best practice · 📦 "create this project now".
  Reuse these; don't invent new emoji semantics. (📎 used to mean "link to the slow version in a sibling
  folder" — that meaning is retired along with the folders.)
- **File references in prose use Markdown links** (`[file.md](path)`), not backticks or bare paths.

---

## 3. The locked stack — never swap, never "modernize" away

.NET 10 · C# · **Modular Monolith + VSA** · **Carter** over ASP.NET minimal APIs · **Dapper + stored
procedures** (NO ORM) · **hand-rolled mediator** (Phase 2 also *compares* the free `martinothamar/Mediator`)
· **CQRS-lite** · **FluentValidation** · **SQL Server**, schema-per-module · **explicit ADO.NET transactions
in handlers** · **JWT bearer + role auth**.

### Banned technologies — do NOT introduce them into lessons or code
Clean Architecture's four-layer project split · **EF Core / any ORM** · Docker · Redis · RabbitMQ ·
MassTransit · any message broker · microservices · distributed caching · a generic `Repository<T>` ·
an `IUnitOfWork` abstraction over the transaction.

> ⚠️ **Known drift:** `course-validation/business/` still uses `IUnitOfWork`, which the lessons dropped in
> favour of raw ADO.NET transactions. The reference build is therefore *behind* the tutorial on this one
> point. Don't copy `IUnitOfWork` from it into a lesson. Bringing the reference build in line is a pending
> task — ask before doing it, since it touches every write handler.

The **only** place these may appear is a lesson's explicit *"when you'd add this later, and why not now"*
discussion (Phase 13's checklist and Phase 14's extensions). Never as part of the built solution.

> **MediatR** specifically is avoided (it moved to a paid license for larger orgs in 2025). Don't
> reintroduce it. The hand-rolled mediator is the lesson; `martinothamar/Mediator` is the sanctioned free
> alternative Phase 2 compares.

---

## 4. The non-negotiable architecture rules

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
   | One module reacts afterward (best-effort) | **Domain event** (dispatched after commit) |
   | "This endpoint is role-X only, full stop" | **Endpoint** (`RequireRole`) |
   | Auth that depends on the data ("your own customers only") | **Handler/domain** via `ICurrentUser` |

   A business `if` in a handler, or a business rule in a validator/stored proc, is a **defect** in this
   course. So is a rule that needs a database to unit-test.

---

## 5. Conventions to preserve when writing or editing code

- **Two files per feature:** `XxxHandler.cs` (Command/Query + Result/DTOs + Validator + Handler) and
  `XxxEndpoint.cs` (Request/Response + Carter `ICarterModule`). **Never** split into technical-layer folders
  (`Commands/`, `Handlers/`, `Validators/`, `Endpoints/`) — that's the anti-pattern VSA rejects.
- **Handlers use the marker interfaces** `ICommandHandler<,>` / `IQueryHandler<,>` (defined in Phase 2), not
  raw `IRequestHandler<,>`. The demo `PingHandler` is the one exception.
- **Base types live in `Erp.Shared.BuildingBlocks`** and are written out in **Phase 1 §4b**: `Entity<TId>`,
  `AggregateRoot<TId>` (both `where TId : struct` — the constraint must be repeated on the derived class or
  it's `CS0453`), `IDomainEvent`, `DomainException`.
- **Strongly-typed IDs** (`readonly record struct SalesOrderId(long Value)`), never bare `long` for an id
  in the domain.
- **Rich domain:** private setters, `Create()` factory + private constructor for rehydration, computed
  `Total` (never a settable field), guarded `Transition`, value objects (`Money`, `Quantity`) that reject
  invalid values in their constructor.
- **Repositories are aggregate-specific** (`ISalesOrderRepository`), never a generic `Repository<T>`.
- **Stored procs are schema-qualified** in both definition *and* every C# call
  (`"sales.usp_SalesOrder_Insert"`, never bare `"usp_..."`).
- **Transactions are raw ADO.NET, explicit and visible in the handler:** open the connection from the
  factory, `conn.BeginTransaction()`, writes run on that transaction, commit at the end; **throw =
  rollback**. There is deliberately **no `IUnitOfWork`** — the transaction boundary stays something the
  reader can see. Read-only validation happens *before* the transaction starts.
- **Domain events dispatch after commit** (best-effort), via `IDomainEvent` / `IDomainEventHandler<T>` /
  `IDomainEventDispatcher`.
- **Role strings are the literal values** `SalesRep`, `SalesManager`, `Admin` — never `"Manager"` or other
  paraphrases (a typo silently breaks an auth check).
- **Money is Philippine Pesos (₱)** throughout.

---

## 6. Intentional simplifications — flagged, not bugs

These are known, documented limitations left as learning exercises. **Do not silently "fix" them in the
base lessons**; they are called out with ⚠️ notes and listed on the Phase 13 checklist / Phase 14 extensions.

- **Credit limit counts only the committed balance**, not in-flight draft/pending orders (Phase 5 §7).
- **`OnHand` never decreases** in the base build (no shipment step; Phase 14 extension).
- **`RequiresApproval` is a surfaced flag**, not a workflow branch (auto-approve is a Phase 14 extension).
- **Domain-event dispatch is best-effort**, not guaranteed (the outbox is a Phase 14 extension).
- **No rate limiting or HTTPS enforcement** in the base build (Phase 13 checklist).

*(Refresh tokens are **not** a deferred simplification — they're built into the base build in Phase 4c:
short access tokens + rotating refresh tokens with reuse detection, keyed to a `SecurityStamp` and capped by
the DB password-expiry.)*

If a change genuinely *implements* one of these, do it as the documented extension and update the
checklist — don't graft it into the base flow where it would muddy a first read.

---

## 7. Pre-edit checklist (run this before changing a lesson)

1. **Does this belong in the locked stack / respect the banned list?** If it adds banned tech to the built
   solution, stop.
2. **Does it keep the "where logic lives" split intact?** No business rules leaking into validators,
   handlers, or stored procs.
3. **Is every type/method I introduce defined and registered, and already familiar to the reader** at this
   point in the sequence (no forward-referencing an undefined API)?
4. **Did I explain the *why*, starting from the pain?** Or did I just show the answer?
5. **Would a reader who knows only what earlier phases taught be able to follow this?** If it assumes
   outside knowledge, either teach it here or cut it.
6. **Does the phase still end in something runnable the reader can verify?**
7. **Did I preserve phase structure, analogies, and callout conventions — and does the prose still sound
   like a person?** (See §2.)
8. **Are `.NET 10` / API claims current?** (e.g. `Microsoft.AspNetCore.OpenApi` is the .NET 9+ default;
   Swashbuckle is now third-party; `IExceptionHandler`, `Rfc2898DeriveBytes.Pbkdf2`, `.slnx` all exist.)
9. **Did I leave any link pointing at a deleted sibling folder?** There must be none.
10. **If I touched the solution layout, feature convention, or the rules — did I update `README.md`?**

When in doubt, **ask or flag** rather than guess. A confused-but-consistent lesson is recoverable; a
silently derailed one teaches the wrong thing.
