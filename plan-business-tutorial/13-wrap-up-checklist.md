# Phase 13 — Wrap-Up & Production-Readiness Checklist

> 🎉 You built a real, secured, rule-enforcing Sales Order module — not a CRUD toy. This final phase
> ties the threads together, gives you an honest production checklist, and points at what to learn next.

---

## 1. What you built (zoom all the way out)

Trace the full lifecycle of one order and notice how *every* pattern from the earlier plans showed up in
service of a real business need:

```
Login (JWT)                          → Phase 4: authentication
  POST /sales-orders                 → Phase 6: create — blocking, credit, category, quantity checks
  PUT  /sales-orders/{id}            → Phase 9: edit a draft (domain enforces "draft only")
  POST /sales-orders/{id}/submit     → Phase 7: atomic inventory reservation + transaction rollback
  POST /sales-orders/{id}/approve    → Phase 8: manager-only role gate + optimistic concurrency + balance
  POST /sales-orders/{id}/cancel     → Phase 8: release reservations (matched pair)
  GET  /sales-orders  /  /{id}       → Phase 10: CQRS-lite reads, no domain, paged search
  (all requests)                     → Phase 11: validation, logging, consistent error responses
```

Every business rule landed in exactly one home:

| Rule | Home | Why there |
|---|---|---|
| Input well-formed | Validator (behavior) | Shape check, before any work |
| Customer blocked | Domain (`Customer.EnsureNotBlocked`) | Pure policy, unit-testable |
| Credit limit | Domain (`Customer.EnsureWithinCreditLimit`) | Pure arithmetic policy |
| Category role / quantity | Domain (`ItemCategory`) | Rules about the category |
| Status transitions | Domain (state machine) | Rules about the order's lifecycle |
| Approval threshold | Domain (`ApprovalPolicy`) | The order's own knowledge |
| **Inventory reservation** | **SQL (atomic proc)** | Only SQL is safe under concurrency |
| **Lost-update protection** | **SQL (`RowVersion`)** | Must be checked atomically at write |
| Manager-only approve | Endpoint (`RequireRole`) | Blanket role gate, before the handler |
| Transaction rollback | Handler (unit of work) | The use case owns its atomic boundary |

If you can explain that table without looking, you have genuinely internalized the architecture. That
was the whole goal.

## 2. The five questions from the beginner plan — answered by real features

[The beginner overview](../plan-beginner-friendly/00-START-HERE.md) said you'd "get it" when you could
explain five things. Here they are, grounded in what you just built:

1. **Why is a command slice shaped differently from a query slice?** Compare
   [Create (Phase 6)](06-create-order.md) — domain, transaction, repository — with
   [Search (Phase 10)](10-queries-search.md) — Dapper straight to a DTO. Writes protect rules; reads
   don't need to.
2. **Which rule lives in the domain vs. SQL, and why?** The table above. Credit = domain; reservation =
   SQL; and you can now say *why* (concurrency safety vs. pure policy).
3. **What did the mediator buy you?** [Phase 11](11-cross-cutting.md): validation, logging, and error
   handling written *once*, applied to all 8 endpoints. Add a 9th and it's covered free.
4. **Where would you (not) add a service?** You added `IPricingService`-style domain logic only where it
   spanned aggregates; you *didn't* add a `SalesOrderService` god-object. The rules live on the entities.
5. **What would it take to split this into its own service?** You already *proved* the module wall: when
   Sales needed stock, it couldn't reach into Inventory — it went through the `IStockReservation` contract
   ([Phase 7](07-submit-reserve-concurrency.md)). Because everything went through `ISender` and the
   contract, and each module owns its own schema, extraction is *largely* mechanical — with **one honest
   exception**: the contract's `IDbTransaction` parameter can't cross a network, so the shared-transaction
   reserve becomes a **saga** (reserve, and release to compensate on failure). Your code doesn't need
   untangling; that one seam does need reshaping. The path — and that caveat — are in
   [beginner Phase 9](../plan-beginner-friendly/10-phase-9-modularity.md).

## 3. Production-readiness checklist (honest about what's still missing)

This tutorial is a *solid, correct* module — but "runs on my machine and passes tests" is not the same
as "in production." Here's the honest gap list, grouped, so you know what a real deployment still needs.
**None of these require the banned technologies** — they're all achievable with the current stack.

### Security
- [ ] Signing key in a **secret store** (user-secrets locally, env var / vault in prod) — never in a
      committed file.
- [ ] **HTTPS enforced** (`app.UseHttpsRedirection()`); tokens must never travel over plain HTTP.
- [ ] Password policy on user creation (length, complexity) — we only *check* passwords here, we didn't
      build user management.
- [ ] Rate-limiting on `/auth/login` to slow brute-force attempts (ASP.NET Core has built-in rate
      limiting — no new dependency).
- [ ] Consider **refresh tokens** so access tokens can be short-lived without users re-logging-in
      constantly. (A known "next step," not built here.)

### Correctness & data
- [ ] Every multi-step write is in a transaction — ✅ you did this. Double-check no new feature skips it.
- [ ] Register the Dapper `TypeHandler`s for strongly-typed IDs at startup — a forgotten one fails at
      runtime.
- [x] A concurrency-safe order-number generator — ✅ done via a SQL `SEQUENCE` +
      `sales.usp_SalesOrder_NextNumber` ([Phase 3](03-database-schema.md)), not a naive `MAX()+1`.
- [x] Matched money/stock pairs — ✅ approval charges the balance and cancellation of an approved order
      reverses it ([Phase 8](08-approval-cancellation.md)); reserve is matched by release.
- [ ] **Credit limit counts in-flight orders, not just approved ones.** As built, the credit check weighs
      one order against the *committed* balance, which only moves at approval — so a customer's stack of
      draft/pending orders doesn't yet count (see the ⚠️ note in [Phase 5 §7](05-domain-model.md)). For real
      use, track **committed + pending exposure** (a `ReservedCredit` column adjusted at submit and released
      on cancel/reject — a matched pair like inventory, or a sum-of-open-orders query). This is the first
      *correctness* upgrade a real deployment needs.
- [ ] **Fulfillment actually ships stock.** As built, submitting *reserves* `Reserved` but nothing ever
      reduces `OnHand` — the warehouse never "ships." A real flow adds an `Approved → Fulfilled` transition
      that atomically *deducts* `OnHand` and clears the matching `Reserved` (see extension exercise #3
      below). Until then, reserved stock is held indefinitely.
- [ ] Decide the reservation policy edge cases: what happens to reservations if an approved order is
      never fulfilled? (A background "expire stale reservations" job — out of scope, but real.)

### Operations
- [ ] **Structured logging** to a real sink (the built-in `ILogger` + a provider — still no new
      framework needed).
- [ ] A **health check** that verifies the database is reachable, not just that the app started.
- [ ] Each module's `Infrastructure/Sql/` scripts run automatically on deploy (DbUp or a simple runner),
      tracked in a `SchemaVersions` table.
- [ ] Configuration validated at startup (fail fast if the connection string or signing key is missing).

### Authorization depth
- [ ] **Row-level authorization** on reads/writes — a SalesRep should see/act on only their own customers'
      orders, a Manager on all. It's a *data-dependent* rule, so it lives in the handler/query
      ([Phase 10](10-queries-search.md) flags this), not an endpoint attribute. The first thing to add for
      a real multi-user deployment.

### API quality
- [ ] Consistent error envelope — ✅ [Phase 11](11-cross-cutting.md) gave you this.
- [ ] Swagger/OpenAPI documented with the auth scheme so testers can paste a token — ✅ mostly there.
- [ ] Pagination caps enforced — ✅ [Phase 10](10-queries-search.md).
- [ ] Idempotency for `submit`/`approve` if clients might retry (an idempotency key) — a thoughtful
      extra.

## 4. What to learn next (and when — not now)

You deliberately did *not* use Docker, Redis, EF Core, RabbitMQ, MassTransit, or microservices. That was
correct for learning this architecture cleanly. Here's *when* each would genuinely earn its place — so
you adopt them for reasons, not fashion:

| Technology | Learn it when… |
|---|---|
| **Docker** | You need identical environments across machines/CI, or easy local SQL Server. Great next step. |
| **Redis** | Profiling shows a genuinely hot read you must cache, or you need distributed rate-limiting. |
| **EF Core** | A different project values developer speed over explicit SQL — *know both*, choose per project. |
| **A message queue (RabbitMQ / a broker)** | You have a *reliable cross-system* reaction (e.g. notify Accounting of every approval) that must not be lost — the outbox pattern from [beginner Phase 9](../plan-beginner-friendly/10-phase-9-modularity.md). |
| **Microservices** | Real scaling/team-autonomy pressure forces it — and your module boundaries (which you kept clean) make the split mechanical. |

> 🧭 **The senior mindset to carry forward:** every one of those tools trades simplicity for a specific
> capability. Adopt one when you feel the *pain* it solves — the same way this whole tutorial introduced
> the mediator only *after* showing the pain of not having one. "We might need it later" is not a reason;
> "we have this exact problem now" is.

## 5. If you want to keep practicing (extension exercises)

Stay within the stack; each reinforces a pattern:

1. **Auto-approve small orders** — branch on `order.RequiresApproval` at submit: over the threshold →
   `PendingApproval` (wait for a manager); at/under it → straight to `Approved` (adjust the balance in the
   same transaction). This turns the high-value *flag* into a real workflow fork. (Reinforces: where a
   policy decision lives, and keeping the balance change atomic — mind the matched pair from Phase 8.)
2. **Reject an order** — a `RejectSalesOrderCommand` (Manager only) that releases reservations, like
   cancel. (Reinforces: state machine, release pair, role gate.)
3. **Shipment / fulfillment (closes the inventory loop)** — as built, stock is *reserved* but `OnHand`
   never drops, so nothing ever truly "ships." Add an `Approved → Fulfilled` transition (new status `5`) that
   **atomically** converts the reservation into a real reduction — both counters move in *one* statement, the
   same safety trick as reserve:
   ```sql
   CREATE OR ALTER PROCEDURE inventory.usp_Inventory_Deduct
       @ItemId BIGINT, @Qty INT
   AS
   BEGIN
       SET NOCOUNT ON;
       UPDATE inventory.Inventory
       SET OnHand = OnHand - @Qty, Reserved = Reserved - @Qty   -- ship: leaves the shelf AND frees the hold
       WHERE ItemId = @ItemId AND Reserved >= @Qty AND OnHand >= @Qty;
       SELECT @@ROWCOUNT;   -- 0 = something's off (nothing reserved to ship); 1 = shipped
   END
   ```
   Add `IStockReservation.DeductAsync`, a `SalesOrder.Fulfill()` domain method (guarded `Approved →
   Fulfilled`), a manager-gated `POST /sales-orders/{id}/fulfill` slice, and the `CK_Inventory_NotOver`
   constraint keeps you honest throughout. (Reinforces: the reserve→deduct lifecycle, and why *every* stock
   move is a single atomic statement.)
4. **Customer statement query** — a read that lists a customer's orders with running credit used.
   (Reinforces: CQRS-lite reads.)
5. **"Only the creator or a manager can cancel"** — a data-dependent authorization rule in the handler
   using `ICurrentUser`. (Reinforces: endpoint vs. handler authorization; the same shape as row-level read
   auth.)
6. **Audit trail table** — a domain-event handler that writes every status change to an `OrderAudit`
   table. (Reinforces: after-commit event dispatch.)

Try one *before* reading how; struggling first is where the learning is.

## 6. Final word from your senior

You started these three folders (maybe) unsure what problem a mediator even solved. You now have a
secured ERP module that enforces credit limits, blocks bad customers, reserves inventory safely under
concurrency, gates approvals by role, and rolls back cleanly on failure — and you can *explain where
every rule lives and why.*

That understanding — not the code — is the thing you built. Code you can always look up; the judgment of
*where things belong* is what makes you an engineer instead of a typist. Be patient with yourself on the
parts that are still fuzzy; re-read, re-type, re-break. Everyone who's good at this got here the same
way: one more pass than the time before.

Go build something real. 🚀

---

## ✅ Final Check Yourself (the capstone)

If you can answer these without notes, this tutorial did its job:

1. Walk an order from login through approval, naming which layer enforces each rule and why.
2. Explain, with the inventory example, why some rules *must* be in SQL and can't be pure C#.
3. Name three things this module still needs before real production — none of which require a banned
   technology.
4. Give one concrete signal that would tell you it's finally time to add a message queue.
5. Point to the single decision (from Phase 2) that would make swapping the mediator engine — or later
   splitting this into its own service — a localized change.

← Back to [the tutorial overview](00-START-HERE.md) · or the [beginner plan](../plan-beginner-friendly/00-START-HERE.md).
