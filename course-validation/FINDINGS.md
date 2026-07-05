# Course Validation — Findings Log

Building both `plan-beginner-friendly` and `plan-business-tutorial` from scratch, as a student would.
Environment: .NET 10.0.301 SDK, SQL Server LocalDB (MSSQLLocalDB, v13 / SQL 2016), Windows 11.

Legend: 🔴 blocker · 🟠 significant · 🟡 minor/polish · 🔵 inconsistency between plans · 💡 suggestion

---

## BEGINNER-FRIENDLY PLAN

### Phase 1 — Skeleton

- 🔴 **No NuGet packages are ever installed.** Phase 1 uses `ICarterModule` / `app.MapCarter()` and
  later phases use FluentValidation and Dapper, but *no phase* runs `dotnet add package Carter`
  (or FluentValidation / Dapper / Microsoft.Data.SqlClient / the Extensions.* abstractions the
  mediator needs). A literal reader hits `CS0246: ICarterModule not found` on the first build.
  Fix: add an explicit "install these packages" step to Phase 1 (and to whichever phase first
  uses Dapper/FluentValidation).

- 🟠 **The DI wiring is described but never shown.** `AddSharedInfrastructure`, `AddMessaging`,
  and `AddSalesModule` are only prose ("scans its own assembly for handlers", "registers the
  behaviors in order"). The genuinely hard part for a beginner — registering an open-generic
  pipeline behavior (`services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>))`)
  and reflection-scanning an assembly for `IRequestHandler<,>` implementations — is left to the
  reader to invent. The `/sales/ping` skeleton literally cannot run without this code, yet Phase 1
  ("run it and see pong") assumes it exists. This is the #1 place a beginner gets stuck.
  Fix: show the ~15 lines of both `DependencyInjection.cs` files.

- 🟡 Code snippets omit `using` directives. Defensible (the plan says "type it, fix the compiler's
  complaints"), but a total beginner won't know `IEndpointRouteBuilder`/`MapGet` need
  `Microsoft.AspNetCore.*`. Worth a one-line note that Carter transitively brings the ASP.NET Core
  framework reference into the class-library module (so a plain `classlib` *can* host endpoints).

- 🟡 `dotnet sln add (Get-ChildItem -Recurse *.csproj)` is given as PowerShell; the bash fallback
  `dotnet sln add $(find . -name *.csproj)` needs the pattern quoted when paths contain spaces.

- ✅ Once packages + DI are supplied, the skeleton compiles and `GET /sales/ping` returns `pong`
  through the real Carter→mediator→handler pipe. Carter's default `AddCarter()` DID discover the
  endpoint living in the *module* assembly (no extra registration needed) — good.

### Phase 2 — Mediator

- ✅ The `Sender` reflection code compiles as written (the `static` lambda capturing `typeof(TResponse)`
  is legal — type parameters aren't captured state). LoggingBehavior + contracts compile and run.
- (Same DI gap as above: §4d never shows the `AddMessaging` body.)

### Phase 3 — Domain

- 🔴 **The base-class code does not compile as written.** In §4:
  ```csharp
  public abstract class Entity<TId> where TId : struct { ... }
  public abstract class AggregateRoot<TId> : Entity<TId> { ... }   // ← as printed
  ```
  `AggregateRoot<TId>` omits the `where TId : struct` constraint its base requires →
  `CS0453: The type 'TId' must be a non-nullable value type`. Every reader typing this exactly
  gets a build error on the very first domain file. Fix: `... : Entity<TId> where TId : struct`.

- 🟠 **Three referenced building blocks have no code, only a bullet in "What to produce":**
  `SalesOrderLine` (used as `new SalesOrderLine(product, qty, unitPrice)` and `l.LineTotal`),
  `ApprovalPolicy.Threshold` (used in `RequiresApproval`), and the three domain events
  (`SalesOrderCreated`, `SalesOrderSubmitted`, `SalesOrderCancelled`). A beginner must infer each
  one's exact shape (constructor signature, that `LineTotal = UnitPrice * Quantity.Value`, that
  `Threshold` is a `Money`, that events take those specific ctor args used at the `Raise(...)` call
  sites). Given this is "the heart of the project," showing these ~15 lines would remove real friction.

- 🟡 The ⚠️ note admits `order.Id` is `SalesOrderId(0)` when `SalesOrderCreated(order.Id, …)` is
  raised, then still prints `Create()` raising exactly that. Consistent with the caveat, but the
  printed code embodies the anti-pattern the note warns against — a beginner copies the flawed line.
  Consider making the printed `Create()` match option (1) (`SalesOrderCreated(customerId)` only).

### Phase 4 — Create Order (end to end)

- 🔴 **Returned id is always 0 as written.** The handler ends with
  `return new CreateSalesOrderResult(order.Id.Value);`, but the printed `AddAsync` computes the new
  `orderId` locally and never puts it back on the aggregate. Worse, it *can't*: `Entity.Id` has a
  `protected set` (Phase 3), so `SalesOrderRepository` (not a subclass of `SalesOrder`) has no legal
  way to assign it. The plan's Phase 3 ⚠️ says "let the repository stamp `Id`… after INSERT," but no
  mechanism to do so is ever provided. As printed, every create returns `salesOrderId: 0`. I added a
  `SalesOrder.SetIdentity(id)` domain method to fix it; the course needs to show this.

- 🟠 **A large amount of code required to "make POST /sales-orders work end to end" is never shown.**
  The deliverable claims a working endpoint, but the plan omits: the `Customer` entity + its
  `EnsureCanOrder()` (only referenced); the `IPricingService` port + a stub impl; the DB schema DDL
  for all four tables (only column *hints* in the insert proc); 3 of the 4 stored procs
  (`usp_SalesOrderLine_Insert`, `usp_SalesOrder_Get`, `usp_SalesOrderLine_GetByOrder`); the
  `IDbConnectionFactory` / `IUnitOfWork` / `IAppTransaction` **implementations** (only interfaces
  shown); and the `GetAsync` aggregate **rehydration** ("rebuild via the private constructor" — but
  with private setters + events-in-`AddLine`, there is no shown way to do it; a `Rehydrate` factory
  or reflection is needed). A motivated beginner will stall on several of these.

- 🟠 **Only one of three needed Dapper type handlers is shown.** `AddAsync` passes `order.CustomerId`
  (a `CustomerId` struct) and `line.ProductId` (a `ProductId` struct) to Dapper, so `CustomerId` and
  `ProductId` type handlers are *required* or Dapper throws at runtime — but only `SalesOrderId`'s is
  shown. Easy to miss; fails only at runtime.

- 🟠 **No exception→HTTP-status mapping exists anywhere in the beginner course.** Confirmed at runtime:
  a blocked-customer `DomainException` and a FluentValidation `ValidationException` both surface as
  **HTTP 500** with a full stack trace. The plan hand-waves ("a later error-handling step would map it
  to 404"), but there is no such phase (0–9 never adds an `IExceptionHandler`/middleware). So in the
  finished beginner build, *every* business-rule rejection is a 500. The business tutorial fixes this
  in Phase 11; the beginner course should at least add a minimal handler or explicitly flag the gap.

- 🟡 `ValidationBehavior` lives in `Erp.Shared.Messaging`, which forces a FluentValidation package
  reference into the Messaging project — never mentioned. (Same missing-package theme as Phase 1.)

- 🟡 Handler comment/property mismatch potential: `SalesOrderLine`'s property name is never fixed by
  the plan, so the `new { line.ProductId, … }` proc-parameter name depends on a property the reader
  invents. If they name it `Product`, the anonymous param no longer matches `@ProductId`.

- ✅ After supplying the omitted pieces, the slice runs end to end against LocalDB: a valid POST returns
  `201 {"salesOrderId":1}` and persists header+line in one transaction; blocked/inactive customers and
  malformed input are correctly rejected by the domain/validator (albeit as 500s — see above).

### Phase 5 — Second module + concurrency

- 🟠 **`SalesOrderLine` property name is load-bearing but never defined.** Phase 5's submit handler
  reads `line.ProductId.Value` and `line.Quantity.Value`, but `SalesOrderLine` was never shown (Phase
  3). A reader who guessed `Product` (a reasonable guess) in Phase 3 now has non-compiling Phase 5
  code. The plan should define `SalesOrderLine` once, explicitly.

- 🟠 **The `rowversion` lost-update guard has no home on the aggregate.** Phase 5 §5 says "the domain
  doesn't manage the rowversion — the repository loads it with the order and passes it back on update,"
  but the shown `SalesOrder` has nowhere to carry it and the repository is stateless/scoped. In
  practice you must add a `byte[] RowVersion` to the aggregate (leaking an infra concurrency token into
  the "pure" domain) or track it out-of-band. The plan asserts a clean separation it never shows how to
  achieve. Worth either showing the pragmatic property + a candid note, or a shadow-tracking approach.

- 🟡 `IStockReservation` is placed in `Erp.Shared.Common/Contracts/` and takes an `IDbTransaction`,
  so `Erp.Shared.Common` (declared to "depend on NOTHING") now pulls in `System.Data`. Fine
  (framework), but the "depends on nothing" claim in Phase 1 is slightly overstated once contracts
  with `IDbTransaction` live there.

- 🟡 `usp_SalesOrder_UpdateStatus` is *redefined* here (adds `@RowVersion`) vs. Phase 4's version. A
  reader building incrementally must know to `ALTER`/replace the proc and add the `RowVersion` column;
  the plan shows the new proc but not the "migrate your Phase 4 DB" step.

- ✅ **Runtime validated end-to-end.** Built the Inventory module, the `IStockReservation` contract in
  Shared, and `SubmitSalesOrder`. Two orders each demanding all 5 units of product 7: one submit
  succeeded (Status→Submitted, stock Reserved=5), the other correctly failed with "Insufficient stock"
  and **stayed Draft with the transaction rolled back** (no oversell, no partial reservation). The
  atomic SQL guard and "throw = rollback" both work exactly as taught. Sales never references Inventory
  — the wall holds and the compiler enforces it. This is the strongest part of the course.

### Phase 6 — Queries (reviewed, not re-run)

- 🟠 **`IDbConnectionFactory.Create()` appears for the first time and contradicts the async style.**
  Phase 4 only *named* `IDbConnectionFactory`; Phase 6's query handler calls `factory.Create()` (sync,
  returns `IDbConnection`) and then `await conn.QueryMultipleAsync(...)`. The interface's actual method
  is never formally defined, and a sync `Create()` sits oddly beside the async `OpenAsync`-style used
  everywhere else. Define the interface once (Phase 4) and be consistent about sync-vs-async.
- 🟠 `usp_SalesOrderDetails_Get` (the two-result-set proc the `GetSalesOrder` handler calls) and the
  entire `SearchSalesOrders` handler + `PagedResult<T>` are listed in "what to produce" but never
  shown — only the search *proc* is. More infer-it-yourself surface.
- ✅ Design (reads skip the domain, go straight to Dapper→DTO) is sound and consistent with CLAUDE.md.

### Phase 7 — Remaining slices + events (reviewed, not re-run)

- 🟠 **The event-dispatch mechanism (`IPublisher`/`Publisher`) is never implemented.** Same gap as the
  mediator's `Sender`: the after-commit snippet calls `publisher.Publish(e, ct)` and defines the
  `INotification`/`INotificationHandler`/`IPublisher` interfaces, but the `Publisher` that finds and
  runs handlers is not shown (and needs the same reflection/DI scan as `Sender`).
- 🟡 **Inconsistent handler snippet.** Step 1's `CancelSalesOrderHandler` constructor takes
  `(orders, stock, uow)` — no `IPublisher` — yet Step 3 shows `foreach (var e in order.DomainEvents)
  await publisher.Publish(...)` "in the command handlers." The reader must reconcile where `publisher`
  comes from.
- 🟡 `SalesOrder.Complete()`, `ReplaceLines()` are referenced but not shown (consistent with the
  Phase-3 pattern of listing domain methods without code).
- ✅ The synchronous-contract vs. integration-event decision content is excellent and matches CLAUDE.md.

### Phase 3 (cont.)
- ✅ After the `where TId : struct` fix, the domain compiles and all recommended unit tests pass
  (empty-submit throws, non-draft submit throws, over-threshold w/o approval throws, Money/Quantity
  reject bad values, Total is computed, Cancelled can't transition). Domain design is sound.

---

## BUSINESS TUTORIAL PLAN

### Phase 1 — Solution setup
- ✅ Much better than the beginner plan on packages: §5 lists every `dotnet add package` and tells you
  to add `<FrameworkReference Include="Microsoft.AspNetCore.App" />` to Infrastructure. The .NET 10
  Swagger/OpenApi note is accurate.
- 🟡 Defers `dotnet new` commands to beginner Phase 1, inheriting that plan's sln-add wrinkle.

### Phase 2 — Mediator (own vs free)
- 🟠 `AddValidatorsFromAssembly(assembly)` needs `FluentValidation.DependencyInjectionExtensions`, which
  Phase 1 §5 never installs (only `FluentValidation` is added to Sales). `AddSalesModule` won't compile
  until that package is added.
- 🟡 `typeof(AddSalesModuleMarker).Assembly` references an undefined `AddSalesModuleMarker` type; the
  reader must create the marker or use `typeof(DependencyInjection)`.
- ✅ Hand-rolled `Sender` compiles; own-vs-free comparison + ValueTask caveats are accurate.

### Phase 3 — Database schema
- 🔴 `identity` is a T-SQL RESERVED KEYWORD — the schema DDL fails verbatim. `CREATE SCHEMA identity;`,
  `CREATE TABLE identity.AppUser`, `DROP TABLE identity.AppUser`, and `REFERENCES identity.AppUser(Id)`
  are all unbracketed in Phase 3. On SQL Server (confirmed LocalDB 2016): `Msg 156: Incorrect syntax
  near the keyword 'identity'`. Must be `[identity]` everywhere (or rename the schema, e.g. `auth`).
  This is the most likely hard block for a business-tutorial reader; it also affects Phase 4's login.
- 🟡 ~10 of ~17 procs are "left as the same shape" (only 6 have bodies). Acceptable for "build it for
  real," but the reader hand-writes GetByUsername/GetById/GetByIds/GetDetail/Search/etc.
- 🔵 Undocumented inconsistency: Phase 3 §2 says approval threshold is "$10k"; beginner uses $50k.
  Not in CLAUDE.md §5's sanctioned-differences table.
- ✅ Once `[identity]` is bracketed, the whole schema (tables, CHECKs, ROWVERSION, SEQUENCE, FORMAT,
  CONCAT, all 6 proc bodies, seed) applies cleanly on LocalDB. SQL is otherwise correct and
  production-shaped.

### Phase 4 — JWT auth (reviewed)
- 🟠 The `LoginHandler` lives in the `Erp.Api` assembly, but the only handler-scanning registration is
  per-module (`AddSalesModule` scans the *Sales* assembly). Nothing scans the host assembly, so the
  mediator resolves `IRequestHandler<LoginCommand,LoginResult>` to nothing → runtime "no service"
  unless the reader also registers it. Phase 4 never mentions this; registration is only alluded to in
  Phase 11 §7. A reader testing login right after Phase 4 gets a runtime failure.
- 🟡 A failed login throws `AuthenticationException`, which (absent the Phase 11 error handler) is a 500,
  not a 401. Consistent with the beginner error-mapping gap; the mapping only arrives in Phase 11.
- ✅ Crypto is correct and current: PBKDF2 via `Rfc2898DeriveBytes.Pbkdf2`, random salt, iteration count
  stored in the hash, `FixedTimeEquals`. The `MapInboundClaims = false` gotcha + explicit
  `RoleClaimType`/`NameClaimType` is exactly right and genuinely the #1 real-world JWT trap. Excellent.
- 💡 Seed hashes are placeholders `<hash-of-Password123!>`; Phase 4 says "run Hash() once and paste."
  A ready-to-paste generated hash (or a one-liner `dotnet run` snippet) would smooth this.

### Phase 5 — Domain model (built + tested)
- 🔴 (inherited) The business plan reuses `Entity`/`AggregateRoot` from BuildingBlocks but never shows
  them — it defers to beginner Phase 3, which is where the `CS0453` missing-constraint bug lives. So the
  business build inherits the same non-compiling base class unless the reader fixes it.
- ✅ Otherwise the domain is the *most complete and correct* part of either course: it fully shows
  `SalesOrder`, `SalesOrderLine`, `Customer`, `ItemCategory`, `ApprovalPolicy`, all 5 events, the state
  machine, and unit tests. Built with corrected base types, all 6 rules-tests pass (no-lines submit
  throws, add-to-submitted throws, high-value flag, blocked customer, credit-limit exceeded, category
  role restriction incl. Admin override). The `Transition` single-gate + guards-before-mutation is clean.
- 🔵 Confirmed cross-plan differences (all sanctioned in CLAUDE.md §5 except the threshold): status enum
  (Draft/PendingApproval/Approved/Cancelled/Rejected vs beginner's 4), `Approve` as its own method (vs
  `Submit(bool approvalGranted)`), `RowVersion`/`ApprovedBy` on the aggregate. The two courses are each
  internally consistent. The one UNdocumented difference is the approval **threshold** ($10k vs $50k).

---

## COVERAGE (honest scope)
- BEGINNER: Phases 1–5 fully built AND run against LocalDB (skeleton→"pong", create-order 201 + persisted,
  cross-module submit + concurrency race: exactly one reservation wins, loser rolls back). Phases 6–9
  reviewed for correctness, not re-run.
- BUSINESS: solution skeleton + mediator built; full Phase-3 schema applied to LocalDB (found the
  `identity` reserved-word bug); Phase-5 domain built + 6 unit tests pass. Phases 6–13 (create/submit/
  approve/cancel/update/queries/cross-cutting/testing/wrap-up) reviewed at the setup level, not built.
