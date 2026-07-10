# Phase 13 — Wrap-Up & Production-Readiness Checklist

> 🎉 You built a real, secured, rule-enforcing Sales Order module — not a CRUD toy. This final phase
> ties the threads together, gives you an honest production checklist, and points at what to learn next.
> (Money throughout is **Philippine Pesos (₱)**.)

---

> ⏱️ **Time for this phase:** ~30 min. Mostly reading and honest self-assessment — trace the lifecycle,
> tick the production checklist, and pick one extension to try. No new code required.

## 1. What you built (zoom all the way out)

Trace the full lifecycle of one order and notice how every pattern you learned showed up because a real
business need demanded it — never for its own sake:

```
Login (JWT) + refresh / rotation     → Phase 4: authentication, rotating refresh tokens, logout
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
| Transaction rollback | Handler (`IDbTransaction`) | The use case owns its atomic boundary |

If you can explain that table without looking, you have genuinely internalized the architecture. That
was the whole goal.

## 1a. The complete project structure (everything you built)

Here's the whole solution once you've worked through Phase 12 — every project, and where each piece lives. Notice
the shape the architecture *forces*: split by **module** (Sales, Inventory) then by **feature** (two files
each), with a pure `Domain/` and a shared kernel — never by technical layer.

```
Erp/                                         # repo root
├── Erp.slnx                                 # the solution (Phase 1)
├── Directory.Packages.props                 # central NuGet versions — one place (Phase 1 §5b)
├── src/
│   ├── Shared/
│   │   ├── Erp.Shared.BuildingBlocks/        # depends on NOTHING
│   │   │   └── BuildingBlocks.cs             #   Entity<TId>, AggregateRoot<TId>, IDomainEvent, DomainException
│   │   ├── Erp.Shared.Common/                # depends on NOTHING — every PORT the modules code against
│   │   │   ├── PagedResult.cs
│   │   │   ├── IClock.cs
│   │   │   ├── Exceptions.cs                 #   NotFoundException, ForbiddenException, ConcurrencyException
│   │   │   ├── Data/IDbConnectionFactory.cs   #   (Phase 3b)
│   │   │   ├── Security/                     #   ICurrentUser, IUserRepository (+AppUser),
│   │   │   │                                 #   IRefreshTokenRepository (+RefreshTokenRecord) — Phase 4
│   │   │   └── Contracts/IStockReservation.cs #   the Sales↔Inventory wall (Phase 7)
│   │   ├── Erp.Shared.Messaging/             # → Common, BuildingBlocks
│   │   │   ├── Contracts.cs                  #   IRequest, ICommand, IQuery, Unit, IRequestHandler,
│   │   │   │                                 #   ICommandHandler, IQueryHandler, IPipelineBehavior, ISender
│   │   │   ├── Sender.cs                     #   the hand-rolled mediator (Phase 2)
│   │   │   ├── DomainEvents.cs               #   IDomainEventHandler, IDomainEventDispatcher + impl (Phase 8)
│   │   │   └── Behaviors/Behaviors.cs        #   LoggingBehavior, ValidationBehavior (Phase 11)
│   │   └── Erp.Shared.Infrastructure/        # → Messaging, Common, BuildingBlocks (+ ASP.NET framework ref)
│   │       ├── Data/SqlConnectionFactory.cs   #   the ADAPTER; its interface lives in Shared.Common
│   │       ├── SystemClock.cs                 #   the IClock adapter
│   │       ├── Security/                     #   IPasswordHasher+PasswordHasher, IJwtTokenService+JwtTokenService,
│   │       │                                 #   IRefreshTokenService+RefreshTokenService, CurrentUser
│   │       ├── Identity/                     #   UserRepository, RefreshTokenRepository — Phase 4
│   │       └── DependencyInjection.cs        #   AddSharedInfrastructure() + AddMessaging()
│   ├── Modules/
│   │   ├── Sales/  (Erp.Modules.Sales)       # → Shared.{BuildingBlocks, Common, Messaging} — NOT Infrastructure
│   │   │   ├── Domain/                        #   pure C#, depends only on BuildingBlocks
│   │   │   │   ├── SalesOrder.cs, SalesOrderLine.cs
│   │   │   │   ├── Customer.cs, ItemCategory.cs, ItemForOrder.cs
│   │   │   │   ├── ValueObjects.cs           #   Money, Quantity, the strongly-typed IDs
│   │   │   │   ├── SalesOrderStatus.cs       #   enum + transition rules
│   │   │   │   ├── ApprovalPolicy.cs, Events.cs
│   │   │   │   └── Ports.cs                   #   ISalesOrderRepository, ICustomerRepository, IItemRepository
│   │   │   ├── Features/                      #   TWO files per feature (Handler + Endpoint)
│   │   │   │   ├── CreateSalesOrder/          #     (Phase 6)
│   │   │   │   ├── SubmitSalesOrder/          #     (Phase 7)
│   │   │   │   ├── ApproveSalesOrder/         #     (Phase 8)
│   │   │   │   ├── CancelSalesOrder/          #     (Phase 8)
│   │   │   │   ├── UpdateSalesOrder/          #     (Phase 9)
│   │   │   │   ├── GetSalesOrder/             #     (Phase 10)
│   │   │   │   └── SearchSalesOrders/         #     (Phase 10)
│   │   │   ├── Infrastructure/
│   │   │   │   ├── SalesOrderRepository.cs, CustomerRepository.cs, ItemRepository.cs
│   │   │   │   ├── EventHandlers/            #   e.g. SalesOrderApprovedAuditHandler (Phase 8)
│   │   │   │   └── Sql/ 001_schema.sql, 002_procedures.sql, 003_seed.sql   # the `sales` + `[identity]` schemas
│   │   │   └── DependencyInjection.cs         #   AddSalesModule()
│   │   └── Inventory/  (Erp.Modules.Inventory) # → Shared.Common only  (Phase 7)
│   │       ├── Infrastructure/
│   │       │   ├── StockReservationService.cs #   implements IStockReservation
│   │       │   └── Sql/ 001_schema.sql, 002_procedures.sql, 003_seed.sql   # the `inventory` schema
│   │       └── DependencyInjection.cs         #   AddInventoryModule()
│   └── Erp.Api/                              # → Modules.{Sales, Inventory}, Shared.Infrastructure
│       ├── Auth/                              #   host/platform auth slices (NOT a module):
│       │   ├── Login/                         #     LoginHandler.cs + LoginEndpoint.cs
│       │   ├── Refresh/                       #     Refresh + Logout + Logout-all handlers/endpoint (Phase 4 §10)
│       │   └── ChangePassword/                #     ChangePasswordHandler.cs + Endpoint (Phase 4 Part 4)
│       ├── Security/ApiKeyMiddleware.cs       #   the client-app gate, first in the pipeline (Phase 4 §11)
│       ├── GlobalExceptionHandler.cs          #   maps exceptions → HTTP status (Phase 11)
│       ├── Program.cs                         #   composition root; ends with `public partial class Program {}`
│       ├── appsettings.json
│       └── appsettings.Development.json
└── tests/
    ├── Erp.Modules.Sales.UnitTests/           #   domain rule tests (fast, the bulk) — Phase 12
    └── Erp.Integration.Tests/                 #   reservation race, rollback, concurrency, e2e workflow — Phase 12
```

> ℹ️ The exact file *splits* are yours — small related types (e.g. all the value objects) can share one
> file, and each two-file feature keeps its Command/Result/Validator/Handler together and its endpoint
> beside it. What matters is the *shape*: module → feature, a pure domain, and the shared kernel.

## 1b. Expected output — a full run of the finished app

With the database created (each module's `Infrastructure/Sql/` scripts applied) and real password hashes in
the seed ([Phase 4](04-jwt-auth.md)), `dotnet run --project src/Erp.Api` gives you `Now listening on…`. Here
is the whole lifecycle against the seed data, with the responses you should get:

```http
# 1) Log in (anyone) — get a short-lived access token + a refresh token
POST /auth/login   { "username": "manager", "password": "Password123!" }
→ 200 OK   { "accessToken": "eyJhbGciOiJI…", "refreshToken": "N0rQ7w…", "username": "manager", "role": "SalesManager" }

# 1b) Later, when the access token has expired — trade the refresh token for a new pair (rotates it)
POST /auth/refresh   { "refreshToken": "N0rQ7w…" }
→ 200 OK   { "accessToken": "eyJhbGciOiJI…", "refreshToken": "8fKp2s…", "username": "manager", "role": "SalesManager" }
#   Replaying the OLD "N0rQ7w…" now → 401 "reuse detected — session revoked" (the whole family is killed)

# 2) Create a draft for Acme (customer 1): 2 × Widget (item 1 @ ₱25).   (Authorization: Bearer <token>)
POST /sales-orders   { "customerId": 1, "lines": [ { "itemId": 1, "quantity": 2 } ] }
→ 201 Created   { "id": 1 }          # Location: /sales-orders/1   (a REAL id, not 0)

# 3) Submit → atomically reserves stock, moves Draft → PendingApproval
POST /sales-orders/1/submit
→ 204 No Content

# 4) Approve (manager only) → PendingApproval → Approved, +₱50 to Acme's outstanding balance
POST /sales-orders/1/approve
→ 204 No Content

# 5) Read it back
GET /sales-orders/1
→ 200 OK   { "id": 1, "orderNumber": "SO-2026-000001", "customerName": "Acme Corp",
             "status": "Approved", "total": 50.00, "lines": [ { "itemName": "Widget", … } ] }
```

And the **rules firing** — the whole point of the module — produce these outcomes:

```http
# A SalesRep tries to approve → rejected BEFORE the handler runs (endpoint role gate)
POST /sales-orders/{id}/approve   (Bearer <rep-token>)
→ 403 Forbidden

# Beta (customer 2) orders 100 × Widget = ₱2,500, but only has ₱500 credit left (domain rule)
POST /sales-orders   { "customerId": 2, "lines": [ { "itemId": 1, "quantity": 100 } ] }
→ 422 Unprocessable Entity
   { "title": "Business rule violated", "status": 422,
     "detail": "Order total ₱2,500.00 would exceed the credit limit. Available credit: ₱500.00." }

# An order for BLOCKED Gamma (customer 3) → domain refuses
POST /sales-orders   { "customerId": 3, "lines": [ { "itemId": 1, "quantity": 1 } ] }
→ 422   { "detail": "Customer 'Gamma Traders' is blocked and cannot place orders." }

# A SalesRep orders the Controlled item (item 3, requires SalesManager) → domain refuses
POST /sales-orders   { "customerId": 1, "lines": [ { "itemId": 3, "quantity": 1 } ] }   (Bearer <rep-token>)
→ 422   { "detail": "Category 'Controlled' requires role 'SalesManager'." }

# Two managers submit orders for the LAST units of item 3 (OnHand 8) at the same instant:
#   exactly ONE wins; the other's whole submit rolls back and the order stays Draft.
POST /sales-orders/{A}/submit  → 204 No Content        (reserved)
POST /sales-orders/{B}/submit  → 422 "Insufficient stock for item 3. Submission cancelled."
```

If your run produces those status codes and messages, **every rule landed in the right home** — the domain
rejects blocked/over-credit/wrong-role orders as `422`, the endpoint gate rejects non-managers as `403`
before any handler runs, the SQL atomic guard makes the concurrency race come out `204`/`422` with no
overselling, and a created order returns a real id. That mapping from *outcome* to *where the rule lives* is
the entire skill this tutorial set out to build.

## 2. Five questions that prove you've got it

You'll know this architecture is really yours when you can explain five things without looking them up.
Here they are, grounded in what you just built:

1. **Why is a command slice shaped differently from a query slice?** Compare
   [Create (Phase 6)](06-create-order.md) — domain, transaction, repository — with
   [Search (Phase 10)](10-queries-search.md) — Dapper straight to a DTO. Writes protect rules; reads
   don't need to.
2. **Which rule lives in the domain vs. SQL, and why?** The table above. Credit = domain; reservation =
   SQL; and you can now say *why* (concurrency safety vs. pure policy).
3. **What did the mediator buy you?** [Phase 11](11-cross-cutting.md): validation, logging, and error
   handling written *once*, applied to every endpoint. Add another and it's covered free.
4. **Where would you (not) add a service?** Notice what you never wrote: a `SalesOrderService` god-object
   holding every rule, with an anemic `SalesOrder` as its data bag. The rules live *on the entities they're
   about* — `Customer.EnsureNotBlocked()`, `ItemCategory.EnsureQuantityAllowed()`. A separate class only
   earns its place when logic genuinely spans several aggregates and belongs to none (a *domain service*),
   and this module never needed one. If a class name ends in `Service` and its methods take the entity as
   their first argument, the logic is on the wrong object.
5. **What would it take to split this into its own service?** You already *proved* the module wall: when
   Sales needed stock, it couldn't reach into Inventory — it went through the `IStockReservation` contract
   ([Phase 7](07-submit-reserve-concurrency.md)). Because everything went through `ISender` and the
   contract, and each module owns its own schema, extraction is *largely* mechanical — with **one honest
   exception**: the contract's `IDbTransaction` parameter can't cross a network, so the shared-transaction
   reserve becomes a **saga** (reserve, and release to compensate on failure). Your code doesn't need
   untangling; that one seam does need reshaping.

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
- [x] A **change-password / reset endpoint** — ✅ built in [Phase 4 Part 4](04d-jwt-change-password.md): a
      `POST /auth/change-password` slice over `usp_AppUser_ChangePassword` that verifies the current password,
      rotates the security stamp (re-login everywhere), and — being anonymous — doubles as the way back in from
      an expired password. The remaining gap is the **forgot-password** path for users who *don't* know the old
      password (an emailed, single-use, time-boxed reset token + rate limiting) — exercise #7 below.
- [ ] Rate-limiting on `/auth/login` to slow brute-force attempts (ASP.NET Core has built-in rate
      limiting — no new dependency).
- [x] **Refresh tokens** — ✅ built in [Phase 4 §10](04c-jwt-refresh-apikey.md): short (15 min) access tokens backed by
      rotating refresh tokens with reuse detection and a revoke-all-sessions endpoint. A password change
      **instantly voids every refresh token** via the user's `SecurityStamp`, and a token's absolute lifetime
      is capped by the DB-driven `PasswordExpiresUtc`, so a session can never outlive its password. (Two
      hardening extras remain, both flagged in that section: delivering the refresh token as an `HttpOnly`
      cookie for browser clients, and a periodic cleanup of revoked/expired rows.)

### Correctness & data
- [x] Every multi-step write is in a transaction — ✅ you did this. Double-check no new feature skips it.
- [ ] Strongly-typed IDs are unwrapped **only** at the repository boundary. A `long` leaking into a handler
      or a domain method is how the compiler's protection quietly stops protecting you.
- [x] A concurrency-safe order-number generator — ✅ done via a SQL `SEQUENCE` +
      `sales.usp_SalesOrder_NextNumber` ([Phase 3](03-database-schema.md)), not a naive `MAX()+1`.
- [x] Matched money/stock pairs — ✅ approval charges the balance and cancellation of an approved order
      reverses it ([Phase 8](08-approval-cancellation.md)); reserve is matched by release.
- [ ] **Editing a draft is version-checked too.** `UpdateStatusAsync` guards against a lost update with
      `RowVersion` ([Phase 8](08-approval-cancellation.md)), but `ReplaceLinesAsync`
      ([Phase 9](09-update-order.md)) does not — two reps editing one draft means the second save silently
      wins. Add `WHERE Id = @Id AND RowVersion = @RowVersion` to `usp_SalesOrder_UpdateTotal` and throw
      `ConcurrencyException` on rowcount `0`. Same pattern, one more proc.
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
- [x] Consistent error envelope — ✅ [Phase 11](11-cross-cutting.md) gave you this.
- [x] Swagger/OpenAPI documented with **both** auth schemes — the bearer token and the `X-Api-Key`
      ([Phase 4c §11](04c-jwt-refresh-apikey.md)) — so a tester can drive every endpoint from the browser.
- [x] Pagination caps enforced — ✅ [Phase 10](10-queries-search.md).
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
| **A message queue (RabbitMQ / a broker)** | You have a *reliable cross-system* reaction (e.g. notify Accounting of every approval) that must not be lost — see the outbox pattern in [Phase 14](14-going-further.md). |
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
   constraint keeps you honest throughout. **Don't forget the schema:** `CK_SalesOrder_Status` currently reads
   `CHECK (Status BETWEEN 0 AND 4)`, so the database rejects status `5` until you widen it — in a *new*,
   numbered script (`004_fulfillment.sql`), never by editing `001_schema.sql`:
   ```sql
   ALTER TABLE sales.SalesOrder DROP CONSTRAINT CK_SalesOrder_Status;
   ALTER TABLE sales.SalesOrder ADD CONSTRAINT CK_SalesOrder_Status CHECK (Status BETWEEN 0 AND 5);
   ```
   The `Msg 547` you'd otherwise hit is that constraint doing its job — the [Phase 3](03-database-schema.md)
   backstop catching a status the schema was never told about. (Reinforces: the reserve→deduct lifecycle, why
   *every* stock move is a single atomic statement, and the append-only migration habit.)
4. **Customer statement query** — a read that lists a customer's orders with running credit used.
   (Reinforces: CQRS-lite reads.)
5. **"Only the creator or a manager can cancel"** — a data-dependent authorization rule in the handler
   using `ICurrentUser`. (Reinforces: endpoint vs. handler authorization; the same shape as row-level read
   auth.)
6. **Audit trail table** — a domain-event handler that writes every status change to an `OrderAudit`
   table. (Reinforces: after-commit event dispatch.)
7. **Forgot-password (emailed reset) flow** — the base build already ships `POST /auth/change-password`
   ([Phase 4 Part 4](04d-jwt-change-password.md)), but it needs the *current* password. Add the path for users
   who've forgotten it: a `POST /auth/forgot-password` that mints a single-use, time-boxed reset token (stored
   as a **hash**, exactly like a refresh token — §10) and "sends" it (log it to the console — a real mail
   channel is out of scope), and a `POST /auth/reset-password` that trades that token for a new password via
   `usp_AppUser_ChangePassword`. Add rate-limiting on both. (Reinforces: the refresh-token hashing/expiry
   pattern reused for a new secret; how the same DB proc backs both the *change* and *reset* paths. See
   [Phase 4 Part 4](04d-jwt-change-password.md).)

Try one *before* reading how; struggling first is where the learning is. ([Phase 14 §4](14-going-further.md)
continues the list at #8, for exercises that push past the base stack.)

> 👉 **Want the production last mile?** [Phase 14 — Going Further](14-going-further.md) is an optional,
> out-of-scope appendix: how you'd close the checklist above, containerize with Docker, cache with Redis, and
> more — for when *you* take this to your own project. It stays inside the same discipline (nothing there
> rewrites the base architecture), so it's safe to read without unlearning anything.

## 6. Final word from your senior

You started this course, quite possibly, unsure what problem a mediator even solved. You now have a
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

← Back to [the tutorial overview](00-START-HERE.md) · onward to [Phase 14 — Going Further](14-going-further.md).
