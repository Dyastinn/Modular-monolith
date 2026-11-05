# Course Validation — Findings Log

Building `plan-business-tutorial` from scratch, as a student would, and checking every lesson against the
reference build in [business/](business/).

Legend: 🔴 blocker · 🟠 significant · 🟡 minor/polish · 💡 suggestion

Newest first. Everything below the 2026-11-06 entry was written for the **retired** SQL Server / Dapper /
hand-rolled-mediator course (phases, not lessons). It's kept as history; its file and phase names no longer
exist.

---

## Production course, lessons 00–16 — 2026-11-06

Environment: .NET SDK 10.0.403, PostgreSQL 18.6 (local), Windows 11, Windows PowerShell 5.1.

**Test suite:** 22 unit + 6 architecture + 25 integration, all passing.

### 🟠 Fixed — a production install had no way to sign in
No seed runs outside Development, and `/auth/register` is blocked, so a fresh install had no users and no
way to create one. Added `InitialAdmin` (Identity module, `IHostedService`): creates an Admin from
`InitialAdmin:Email`/`Password` only while there are no users at all. Proven by
`A_fresh_install_gets_its_first_admin_from_configuration` on its own empty, unseeded database. Taught in
lesson 15.

### 🟠 Fixed — a 400's `detail` leaked FluentValidation's developer message
`detail` was `ex.Message`: `Validation failed: -- Lines: An order needs at least one line. Severity: Error`.
It's now the messages joined into a sentence (they're also in `errors`). The validator regression test now
asserts the detail. Lesson 06 updated.

### 🟡 Fixed — `₱` garbled in Windows PowerShell 5.1
Problem details were sent as `application/problem+json` with no charset, and PS 5.1 doesn't assume UTF-8.
Now `; charset=utf-8`, asserted by the same test.

### 🟡 Fixed — smaller lesson items
- Lesson 13 said the eleventh sign-in gets a 429, ignoring the sign-in made just before in the same minute.
- Lessons 13 and 14 gave `create-roles.sql` a repo path (`course-validation/business/...`) instead of a path
  relative to the reader's solution.

### 🟡 Open — data-protection keys stored unencrypted
On first start in Production the app warns `No XML encryptor configured`. True: the keys in
`identity.data_protection_keys` are plain XML. Listed in lesson 16 and ROADMAP M7 (protect with a
certificate before the first sale).

### ✅ Verified live
- **Migration bundles**: `dotnet ef migrations bundle` per module; `--connection` overrides the design-time
  factory's placeholder `DbConnection`. All three applied as `erp_owner` to a database made by
  `create-roles.sql`. The first run logs a harmless `Failed executing DbCommand` for the missing history table.
- **The published app in Production as `erp_app`**: `/health/live` and `/health/ready` 200; no API key 401;
  `/auth/register` 404; the initial admin signs in and `/auth/me` says Admin; the order list is empty;
  security headers present.
- **Backup and restore**: `pg_dump -Fc` as `erp_owner`; then database and roles dropped, `create-roles.sql`
  run again as on a new server, `pg_restore` as `erp_owner` with no extra flags. `erp_app` can read, insert,
  call the SQL functions and use the order-number sequence. The lesson 16 restore drill (scratch database
  owned by `erp_owner`) also works.
- **The PowerShell companion** (00b): the setup block and the checkpoints for lessons 05–13 run on Windows
  PowerShell 5.1 against the reference API, with the statuses and messages the lessons promise.

### Not verified
- **IIS itself.** There's no IIS on this machine. Lesson 15's IIS steps (features, Hosting Bundle, app pool,
  environment variables, bindings, `removeServerHeader`) are written from Microsoft's documentation and need
  one run on a real Windows Server 2019+ before the first sale.
- **Lessons followed from an empty folder.** Each lesson's code was checked against the reference build, not
  rebuilt from scratch in sequence.

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
- ✅ (**fixed**) The business plan used to reuse `Entity`/`AggregateRoot` from BuildingBlocks without ever
  showing them, deferring to the (now deleted) beginner Phase 3 — which also carried a `CS0453`
  missing-constraint bug. Both types are now written out, correctly constrained, in business
  **Phase 1 §4b**, alongside `IDomainEvent` and `DomainException`.
- ✅ Otherwise the domain is the *most complete and correct* part of either course: it fully shows
  `SalesOrder`, `SalesOrderLine`, `Customer`, `ItemCategory`, `ApprovalPolicy`, all 5 events, the state
  machine, and unit tests. Built with corrected base types, all 6 rules-tests pass (no-lines submit
  throws, add-to-submitted throws, high-value flag, blocked customer, credit-limit exceeded, category
  role restriction incl. Admin override). The `Transition` single-gate + guards-before-mutation is clean.
- ✅ The tutorial's status enum (Draft/PendingApproval/Approved/Cancelled/Rejected), `Approve` as its own
  manager-gated method, and `RowVersion`/`ApprovedBy` on the aggregate are internally consistent throughout.

---

## COVERAGE (honest scope)
- BUSINESS: solution skeleton + mediator built; full Phase-3 schema applied to LocalDB (found the
  `identity` reserved-word bug); Phase-5 domain built + 6 unit tests pass. Phases 6–13 (create/submit/
  approve/cancel/update/queries/cross-cutting/testing/wrap-up) reviewed at the setup level, not built.

---

## Sequential course review — 2026-07-10 (Phases 0–3b, 5–8, 11, 13 + cross-phase sweeps)

Reviewed in course order against the reference build. Reference build re-verified after every change:
**`dotnet build` clean, 14/14 unit tests, 7/7 integration tests.**

### 🟠 Fixed — modules referenced `Erp.Shared.Infrastructure` for nothing
`Erp.Modules.Sales` and `Erp.Modules.Inventory` both carried a `ProjectReference` to
`Erp.Shared.Infrastructure`. **No source file in either module ever used it** (verified: no
`using Erp.Shared.Infrastructure;` anywhere under `src/Modules/`), and the reference handed each module
compile-time access to `SqlConnectionFactory`, the JWT/password services, and the `HttpContext`-reading
`CurrentUser` — flatly contradicting Phase 1's own "the missing reference *is* the wall" thesis and its
"a module's Domain never sees it" table. Removed from both `.csproj`s; Inventory now declares the
`Microsoft.Extensions.Configuration.Abstractions` package it had been getting transitively.
Lesson side: `01-solution-setup.md` §3 command + graph + §4 table + §9 + Check Yourself,
`07-submit-reserve-concurrency.md` §3 (Inventory `dotnet add reference`), `13-wrap-up-checklist.md` §1a tree.

### 🔴 Fixed — `NotFoundException` used in Phases 6–9, defined only in Phase 11
A reader typing Phase 6's `CreateSalesOrderHandler` hit `CS0246`. Same for `ConcurrencyException`
(used in Phase 8's repository, defined in Phase 11). Now introduced at first use — `NotFoundException` in
Phase 6 §5, `ConcurrencyException` in Phase 8 §4 — with Phase 11 §6 adding only the genuinely new
`ForbiddenException` (which nothing in the base build throws; noted explicitly).

### 🟠 Fixed — Phase 3's checkpoint could not pass
Phase 3 said the Inventory SQL scripts arrive in Phase 7, then its checkpoint told the reader to
`SELECT ItemId, OnHand FROM inventory.Inventory`. Phase 7, meanwhile, said those scripts were "the three
scripts you wrote in Phase 3." Phase 3 now creates `src/Modules/Inventory/Infrastructure/Sql/` (folder
only — the project arrives in Phase 7), runs all six scripts in dependency order, and creates the `Erp`
database first. Matches `DatabaseInitializer`, which applies Sales before Inventory for the same reason.

### 🟠 Fixed — `DomainException` mapped to 400 in Phase 1, 422 everywhere else
Ground truth (`GlobalExceptionHandler`) is **422 Unprocessable Entity**; 400 is `ValidationException`.
Corrected Phase 1 §4b.

### 🟡 Fixed — smaller inconsistencies
- Phase 2 prose showed `AddScoped<IRequestHandler<…>>` while its own registration code (and the reference
  build) uses `AddTransient`. Corrected, and added the missing explanation of *why* handlers are transient,
  `ISender` scoped, and the password hasher singleton.
- Phase 3b's checkpoint said "the factory and **Dapper** work end-to-end" in code that explicitly avoids
  Dapper ("we haven't met Dapper's API yet").
- Phase 5's `Create()` note offered "have the repository stamp `Id`" as a clean fix — impossible, since
  `Entity<TId>.Id` has a `protected set`, exactly as Phase 6's note says. Reworded; also states plainly
  that `SalesOrderCreated.Id` is always `0` and nothing consumes the event.
- Phase 5's state-machine ASCII diagram had an unclosed `Cancelled` box. Redrawn.
- Phase 0's endpoint table omitted `Admin`, which every `RequireRole(...)` in Phases 6–8 grants (and which
  bypasses the category rule). Table corrected + superuser note added.
- Phase 13 extension #3 (fulfillment) adds status `5` but never mentions `CK_SalesOrder_Status`
  `CHECK (Status BETWEEN 0 AND 4)`, so the attempt dies on `Msg 547`. Added the guarded `ALTER`.
- Stale comment in `Erp.Modules.Sales.csproj` claimed the `FrameworkReference` was "a gap in the tutorial";
  Phase 2 §4 Step 1 teaches it.

### ✅ Verified consistent (no change needed)
- Role strings are the literal `SalesRep`/`SalesManager`/`Admin` throughout; no `"Manager"` paraphrase.
- Every stored-proc call string is schema-qualified; no bare `usp_…`.
- Approval threshold is ₱50,000 in every phase, the glossary, and the tests.
- `SalesOrderStatus` 0–4 agrees across `00a-FOUNDATIONS`, Phase 3's `CHECK`, Phase 5, and the build.
- The auth story holds end to end: `sub` is written by `JwtTokenService` and read by `CurrentUser`, with
  `MapInboundClaims = false` keeping the claim types aligned with `RoleClaimType`/`NameClaimType`.
- `Jwt:RefreshAbsoluteDays` appears only as a *named anti-pattern* (Phases 4, 4c, 4e); the absolute cap
  really is the user's `PasswordExpiresUtc`. No contradiction.
- Behavior order (`LoggingBehavior` registered before `ValidationBehavior` ⇒ logging outermost) agrees
  between Phase 2 §7/§9, Phase 11, and the reference build.

### COVERAGE (honest scope) — Part 1
Deep, line-by-line: Phases 0, 0a, 1, 2, 3, 3b, 5; targeted on 6, 7, 8, 11, 13; plus mechanical cross-phase
sweeps (proc names, role strings, threshold, status values, JWT config keys, define-before-use for 11 key
types, per-phase Checkpoint/Check-Yourself/Common-Mistakes presence) covering **all** phases.

---

## Sequential course review, part 2 — 2026-07-10 (Phases 4b–4e, 9, 10, 12, 14 + glossary/resources)

Completes the pass. Reference build re-verified: **build clean, 14/14 unit, 7/7 integration** (one new test).

### 🔴 Fixed — `RefreshHandler` did not compile (CS0136)
Phase 4c printed `using var conn = factory.Create();` inside the reuse-detection `if` block **and** again at
method scope for the rotation. C# forbids that: *"A local named 'conn' cannot be declared in this scope
because that name is used in an enclosing local scope."* Verified with a minimal repro. The reference build
had already worked around it by naming the first one `breachConn`; the lesson had not. Lesson now matches,
with a comment saying why.

### 🔴 Fixed — `GET /sales-orders` returned 400 for the plainest possible call
The search endpoint bound `int page, int pageSize`. A **non-nullable** `int` query parameter is *required* by
minimal-API model binding, so a bare `GET /sales-orders` failed with 400 before the handler ran — meaning the
`page == 0 ? 1 : page` guard was unreachable dead code and `SearchSalesOrdersQuery`'s `Page = 1` default was
never used. Verified empirically against a scratch .NET 10 app. Changed to `int? page, int? pageSize` +
`?? 1` / `?? 20` in **both** the lesson (Phase 10 §6, with a ⚠️ explaining the two-layers-of-defaults trap)
and the reference build. Added integration test `Search_with_no_query_string_returns_the_first_page`, and
confirmed it **fails** against the old signature before fixing it.

### 🔴 Fixed — `dotnet new xunit` cannot build under Central Package Management
Phase 5 §10 said the template "already pulls in xUnit + the test SDK" and moved straight to running the
tests. In fact the template writes four **versioned** `PackageReference`s, which CPM rejects:
`error NU1008: … cannot define a value for Version: coverlet.collector, Microsoft.NET.Test.Sdk, xunit,
xunit.runner.visualstudio`. Reproduced from scratch. Phase 5 now shows the fix (strip the versions, add the
`<PackageVersion>` lines); Phase 12 carries a short reminder when it scaffolds the integration project.
This is why the reference build's test `.csproj`s are version-less.

### 🟠 Fixed — Phase 4's checkpoint promised 401s the app cannot yet return
Every `401 "<message>"` in Phase 4 (bad password, expired password, refresh reuse, revoked token) comes from
a **thrown** `AuthenticationException` — and no global exception handler exists until Phase 11, so those
requests actually return **500**. The middleware-produced 401s (API-key gate, JWT bearer) *are* real. Added
an honest ⚠️ at the throw site (4b), at the top of the checkpoint (4e), and on 4d's expired-account trace.
The same note explains that `LoginValidator`/`ChangePasswordValidator` are registered but **never executed**
until Phase 11 adds `ValidationBehavior` — previously an unmentioned phantom.

### 🟠 Fixed — Phase 12's race test as printed did not compile, and was not re-runnable
`private Task<int> GetReserved(long itemId) => /* SELECT … */ ;` is not valid C# ("bodies elided for
brevity" — but they're on the critical path). Inlined the reference build's real bodies. Also added the
`ResetReservation` **at the start** of the test, which the reference has and the lesson omitted: the test
commits, so a previous failed run leaves `Reserved = 5` and the next run fails for an unrelated reason.
Phase 12's package list was also missing `Dapper`, which those helpers use.

### 🟠 Fixed — Phase 9 silently dropped the `RowVersion` guard Phase 8 had just taught
`ReplaceLinesAsync` rewrites an order's lines with no optimistic-concurrency check, so two reps editing one
draft means the second save silently wins. It was flagged nowhere. Added the ⚠️ to Phase 9 §5 (naming what
atomicity does and does not buy) and a Phase 13 checklist entry with the fix.

### 🟡 Fixed — smaller items
- Phase 4b explained `MapInboundClaims = false` but never why `RoleClaimType` is then the **long URI**. Added
  the explanation (the token literally carries `ClaimTypes.Role` as its claim type, because `JwtTokenService`
  constructs `JwtSecurityToken` directly and no outbound mapping runs). Confirmed empirically: `manager` gets
  204 on approve and `rep` gets 403, which only holds if the types agree.
- Phase 4e's request-flow trace said the role gate was "SalesRep or Manager" — the forbidden paraphrase.
  Now "SalesRep, SalesManager or Admin".
- Reference-build comments cited stale section numbers (`Phase 4 §9`, `§9b`, `ICurrentUser` as `§2`).
  Phase 4's five parts are numbered continuously §1–§15; pointers corrected across 10 files.
- Test counts updated to 14 + 7 in `CLAUDE.md`, `README.md`, and this log.
- `GLOSSARY.md` called the shared kernel "the building blocks *every* module may use" and listed
  `Infrastructure` among them — the same error the Phase 1 fix corrected. Reworded (and the "locked floor"
  analogy extended: floors use the power sockets, nobody but the manager visits the generator room).

### ✅ Verified consistent (no change needed)
- Phase 4's five parts number their sections continuously (§1–§15) and each cross-reference resolves.
- `LoginResult` is correctly extended from 3 to 4 members in Part 3, before `RefreshHandler` uses it.
- `ApiKeyMiddleware` in the lesson is byte-identical to the reference build.
- The canonical analogies are unique and unduplicated: mediator = post office, atomicity = turnstile,
  JWT = festival wristband.
- No banned vocabulary (`INotification`, `IPricingService`, `tx.Inner`, `Submit(bool approvalGranted)`) and
  no banned technology (EF Core, MediatR, MassTransit, RabbitMQ, Redis) in the lessons or the built solution.
  `IUnitOfWork` appears only in Phase 3b's "why we don't use one" discussion.
- Only two `using var conn` shadowing sites existed course-wide; the other (UserRepository) is two separate
  methods and is fine.

### COVERAGE — now complete
Every phase (0, 0a, 1, 2, 3, 3b, 4, 4b, 4c, 4d, 4e, 5–14) plus `GLOSSARY.md` and `LEARNING-RESOURCES.md` has
been read and reviewed in course order. The `03a-database-queries/` companion was checked against Phase 3
(guarded statements, `Identity`/`Sales`/`Inventory` split, documented run order, `CHECK (Status BETWEEN 0 AND
4)`, seed values) rather than read line by line; `04a-password-hashing/` was not exercised.

Three defects were verified by **reproducing them**, not by reading: `CS0136` on the refresh handler, the
`400` on a bare `GET /sales-orders`, and `NU1008` from `dotnet new xunit` under CPM. The search fix is now
guarded by an integration test that was confirmed to fail before the fix.

---

## Per-part checkpoints for Phase 4 — 2026-07-10

Phase 4 is ~3.5 hours across five files and had **one** checkpoint, at the very end of Part 5. A reader whose
login failed there had five parts of unverified code to search. Added a runnable checkpoint to each part, and
verified every command against the running reference API before writing it down.

### 🔴 Found while writing them — `IPasswordHasher` and `IJwtTokenService` were never registered
Both are built in Part 1 and injected by `LoginHandler` in Part 2, but the only `AddSingleton`/`AddScoped`
lines for them appear in **Phase 11**'s consolidated composition root. Following the lessons in order,
`POST /auth/login` would throw *"No service for type `IPasswordHasher` has been registered."* on the first
request. Part 2's registration block now adds all three (`IPasswordHasher`, `IJwtTokenService`,
`IUserRepository`) with an explanation of why the hasher is a singleton and the repository scoped.

### What each checkpoint proves
- **Part 1** — a throwaway `/dev/token` endpoint (same trick as Phase 3b's `/db-ping`) mints a token and reads
  it back with `ReadJwtToken`. Verified the endpoint compiles from `Erp.Api` (`System.IdentityModel.Tokens.Jwt`
  arrives transitively through `Erp.Shared.Infrastructure`) and captured the real claim list. It shows `sub`
  surviving as `sub` while `name`/`role` carry the long `http://schemas…` URIs — which is exactly the setup for
  Part 2's `MapInboundClaims` lesson. Also verified the failure mode: a blank `Jwt:SigningKey` gives
  `ArgumentException: IDX10703 … key length is zero`, a missing one an `ArgumentNullException`.
- **Part 2** — adds `.RequireAuthorization()` to `/sales/ping` (previously only mentioned as a prerequisite in
  Part 5), then proves anonymous → 401, garbage token → 401, valid token → 200, and that both login failures
  return the *same* generic message. No API key yet, and login returns **no** `refreshToken` — both true only
  at this point in the course.
- **Part 3** — the API-key gate, rotation, reuse detection, and logout.
- **Part 4** — the anonymous change-password, and that rotating `SecurityStamp` *is* the global logout.

### 🟠 Two of my own claims were wrong until I ran them
- I first wrote that replaying a spent refresh token kills the family, so the honest client's rotated token
  then reports *"Invalid refresh token."* It does not: `RevokeFamilyAsync` stamps `RevokedUtc` on **every**
  token in the lineage, so the rotated one takes the same reuse branch and reports *"Refresh token reuse
  detected."* Confirmed against the running API; the lesson now explains why.
- A troubleshooting note blamed the `MapInboundClaims` trap for a 401 on `/sales/ping`. It cannot be —
  that endpoint only asks "are you authenticated?" and never reads `ICurrentUser` or a role. Rewritten to
  point at `ValidIssuer`/`ValidAudience`/`IssuerSigningKey`, and to say plainly that the claim-name trap only
  surfaces once a role check or `UserId` read exists.

### 🟠 Fixed — "re-run the Phase 3 seed" was bad advice
Part 5 told readers to re-seed after changing `rep`'s password. The seed `INSERT`s users; it never updates
them, so re-running it either trips the `UNIQUE` constraint on `Username` or (with the `03a` guards) does
nothing. Both Part 4 and Part 5 now restore the password through the change-password endpoint, which is the
only sanctioned path — and, as a bonus, pushes `PasswordExpiresUtc` 90 days out.

### Verified live against the reference API
Started `Erp.Api` on LocalDB and ran every command: no key → `401 {"error":"Missing or invalid API key."}`;
login → `accessToken`/`refreshToken`/`username`/`role`; refresh → a different refresh token; replay → reuse
detected; the rotated token → reuse detected; logout → `204`, idempotent on a repeat **and** on an unknown
token; change-password anonymously on an **expired** password → `204`, then login → `200`.

> 💡 Incidental: the local dev `Erp` database had `rep` and `admin` with a lapsed `PasswordExpiresUtc`, so
> they could not log in. Repaired through the change-password endpoint (the very flow Part 4 teaches). All
> three seed users now authenticate and expire ~90 days out. `Erp_Test` was untouched.
