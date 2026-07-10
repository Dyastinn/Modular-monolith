# Course Validation — Findings Log

Building `plan-business-tutorial` from scratch, as a student would.
Environment: .NET 10.0.301 SDK, SQL Server LocalDB (MSSQLLocalDB, v13 / SQL 2016), Windows 11.

Legend: 🔴 blocker · 🟠 significant · 🟡 minor/polish · 💡 suggestion

> **Note:** this log predates the removal of the `plan-beginner-friendly` and `plan-Opus` folders. Findings
> that referred to those courses have been dropped; findings about the business tutorial are kept as-is,
> including ones since fixed.

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
