# Best-practices review — 2026-11-13

A check of the course's locked decisions ([CLAUDE.md](CLAUDE.md) §3–5) against what Microsoft and the main
.NET practitioners actually do. Each item gives the evidence, a verdict, and what would change. Nothing
here is applied yet; the decisions at the end come first, then this becomes ROADMAP milestone M9.

Verdicts: **Change** (the evidence is clear) · **Your call** (credible sources disagree) · **Keep** (already
matches practice).

## Decisions (user, 2026-11-13)

- Every **Change** item is accepted.
- **Reads (item 9): keep option A.** Dapper + SQL functions shipped by migrations stay as they are.
- **Event split (item 6): yes.** Domain events stay inside the module, and integration events in Contracts
  are mapped from them.
- **Front end (item 11): yes.** Move to a Blazor Web App with interactive WebAssembly.
- **New: error codes (item 16).** Every error a user can see carries a stable ID with a documented cause and
  fix.

Where a claim comes from my own knowledge rather than a source found today, it says so, and the spike in
step 1 of the plan checks it.

---

## Summary

| # | Area | Today | Verdict |
|---|---|---|---|
| 1 | Request mediator | `martinothamar/Mediator`; `Erp.Api` sees every module's internals | **Change** |
| 2 | Endpoints | Carter, hand-registered because modules are internal | **Change** |
| 3 | Validation | FluentValidation via a mediator behavior that throws | **Change** (keep FluentValidation) |
| 4 | Not-found handling | 33 `throw new NotFoundException` → 404 | **Change** |
| 5 | Domain rule violations | `DomainException` → 4xx | **Keep** |
| 6 | Events | Outbox → Mediator `INotification`; aggregates raise public Contracts types | **Change** the delivery and split the types (decided) |
| 7 | Clock | Custom `IClock` | **Change** → `TimeProvider` |
| 8 | API docs | Swashbuckle | **Change** → `Microsoft.AspNetCore.OpenApi` |
| 9 | Reads | Dapper + SQL functions shipped by migrations; EF banned for reads | **Keep** (decided) |
| 10 | Cross-module transaction | Shared connection, explicit transaction | **Keep** |
| 11 | Front end | Hand-built "hosted Blazor WASM" | **Change** → Blazor Web App (decided) |
| 12 | Browser auth | Identity cookie, same origin | **Keep** |
| 13 | Migrations | Bundles in production, runtime only in Development | **Keep** |
| 14 | Test framework | xUnit 2.9.3 | **Change** → xUnit v3 (after a Stryker spike) |
| 15 | Build hygiene | `Directory.Build.props` has no analyzer or warning settings | **Change** |
| 16 | Error codes | Errors carry a title and a per-request `traceId`, but no stable ID | **Add** (user request) |

---

## 1. Request mediator — Change

**Evidence.** The Mediator README says to install the source generator "in edge/outermost executable
projects" and not in every project. It documents no way for a class library to generate or register its own
internal handlers, and every example uses public handlers. The generated `AddMediator` in the host has to
name each handler, so the handlers must be public or visible through `InternalsVisibleTo`. A user in the
project's issue tracker reached the same workaround we did and called keeping handlers private to their own
assemblies "cleaner". Practitioners who build vertical slices without MediatR inject the handler into the
endpoint and move cross-cutting work to endpoint filters (Jovanović, Anton DevTips).

**What we actually use it for.** All 43 `Send` calls except one come from endpoints. Each message has exactly
one handler. Modules call each other through Contracts interfaces, not the mediator. The two behaviors
duplicate the ASP.NET Core OpenTelemetry instrumentation (item 15 of observability) and an endpoint filter
(item 3).

**Your recommendation was right.** Each module's public `AddXxxModule()` registers its own internal handlers,
and `Erp.Api` sees nothing else. The change:

- Handlers become plain `internal sealed class XxxHandler(...)` with a `HandleAsync` method, registered in
  `AddXxxModule()`. Commands and queries stay as message records, so the CQRS-lite split is still visible.
- Endpoints inject the handler directly. "Go to definition" works again.
- `InternalsVisibleTo Erp.Api` is removed from every module. The architecture test then allows only test
  projects, and a new test checks that `Erp.Api` references only public module types.
- `Mediator.Abstractions` and `Mediator.SourceGenerator` leave the build. The `options.Assemblies` list
  disappears, and with it the trap where a missing assembly silently drops events.

**The honest cost.** You lose one automatic "every request gets X" hook. A route-group endpoint filter
replaces it for HTTP. There is no other entry point today, so nothing else needs it.

## 2. Carter — Change

**Evidence.** Microsoft's guidance for organizing minimal APIs is `MapGroup` inside static extension
methods, one per area. Carter's main feature is discovering endpoint modules automatically, and it can't
see our internal types, so every module already registers its endpoints by hand.

**Change.** Each module exposes `MapSalesEndpoints(this IEndpointRouteBuilder)` beside `AddSalesModule()`,
maps its features into `api.MapGroup("").WithTags("Sales")`, and keeps its existing URLs (`/api/customers`,
not `/api/sales/customers`, so no client breaks). Validation is one filter on the host's `/api` group, and
authorization stays per endpoint, because roles differ endpoint by endpoint. One package goes, and the module's public surface is two methods, the same shape on both the services and the
routes side.

## 3. Validation — Change how it runs, keep FluentValidation

**Evidence.** .NET 10 has built-in minimal API validation (`AddValidation()`), but it runs Data Annotations,
and a third-party write-up says the validated models must be public. The spike went further: it validated
**only** a public model in the host project. An internal model, even in the host, and a module's endpoint
were **skipped silently**: `customerId: 0` was accepted with a 200. For internal modules that's worse than
not having it, so it stays out, and lesson 03 says why in one sentence. Endpoint filters are the standard minimal API hook for this, and they're what
the MediatR-free VSA write-ups use.

**Change.** A generic `ValidationFilter<T>` (BuildingBlocks) resolves `IValidator<T>` and returns
`TypedResults.ValidationProblem(...)`, so the response is the same RFC 9457 body. No exception is thrown for
a malformed request. Validators stay in the handler file and are registered with
`includeInternalTypes: true`.

## 4. Not found — Change

**Evidence.** Microsoft's "Best practices for exceptions" says to "handle common conditions to avoid
exceptions". A missing order id is a routine outcome, not an exceptional one. `TypedResults.NotFound()`
exists for exactly this.

**Change.** Query handlers return `null` (or a small result) when the thing doesn't exist. Endpoints return
`Results<Ok<T>, NotFound>`. `NotFoundException` is deleted. Separately, the endpoints use `Results.*` (77
calls). Microsoft's minimal API docs say "returning `TypedResults` is preferred to returning `Results`": the
concrete types carry the OpenAPI response metadata and can be checked directly in a unit test. The spike
confirmed it: with `Results<Ok<OrderDto>, NotFound>` the OpenAPI document lists 200 and 404 with no
`Produces` call.

## 5. DomainException — Keep

Throwing when an aggregate refuses an invalid state change (credit limit, wrong status) is standard DDD
practice, and the same Microsoft page also says to throw rather than return error codes. A Result type
everywhere is a real alternative, but it's a debate, not a consensus, and it adds ceremony to every method.
The domain keeps throwing, and `GlobalExceptionHandler` keeps mapping it to a problem response.

## 6. Events — Change the delivery; your call on splitting types

**Delivery (Change).** With the mediator gone, the outbox needs its own delivery: an
`IIntegrationEventHandler<T>` interface in BuildingBlocks, about 20 lines in `OutboxDispatcher` that resolve
every handler for the event's type from DI, and each module registering its own handlers. `IDomainEvent` no
longer inherits a library type, so BuildingBlocks stops depending on Mediator.

**Splitting types (your call).** Today an aggregate raises a public Contracts type directly. For example,
`Item` raises `ItemCatalogChanged` from `Inventory.Contracts`. The common guidance (Jovanović, and Microsoft's
older DDD guidance from De la Torre) keeps **domain events** inside the module and maps them to public
**integration events** that cross the boundary. Splitting them means another module's needs can't reshape
your aggregate, at the cost of one mapping class per event (six today). I recommend splitting, because
"islands with one service window" is the course's central idea.

## 7. Clock — Change

**Evidence.** .NET 8 added `System.TimeProvider`. Microsoft's docs present it as the time abstraction, and
`FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`, MIT) is the test double. Teams are deleting
their own `IClock` wrappers in favor of it (Balliauw, 2026).

**Change.** Inject `TimeProvider` and register `TimeProvider.System`. Tests use `FakeTimeProvider`.
`IClock` is deleted.

## 8. API docs — Change

**Evidence.** Microsoft removed Swashbuckle from the Web API template in .NET 9 and ships
`Microsoft.AspNetCore.OpenApi` (`AddOpenApi` / `MapOpenApi`). Sources disagree on how well Swashbuckle is
maintained today, but the framework's own package is the documented path.

**Change.** Use `AddOpenApi()` + `MapOpenApi()` in Development, with Scalar (MIT) or the raw document for a
UI. Check the licence first and record it in the notices file.

## 9. Reads — Your call

**Evidence.** Microsoft's own CQRS reference (eShopOnContainers) uses Dapper for reads, so the Dapper read
side is legitimate. But the same guide says EF Core or plain ADO.NET are equally fine, and it warns against
applying CQRS everywhere. eShop writes its SQL **inline in C#**. It does not put each read in a database
function shipped by a migration. Benchmarks of EF Core `AsNoTracking()` + `Select` projections against Dapper
disagree (from about 5% slower to 50–200% slower), so speed doesn't settle it.

**What the SQL functions cost.** Every read needs a `.sql` file, a migration, and a `.v2` file plus a new
migration for any change. Column and parameter mistakes surface only at runtime. And the logic lives in the
database, the habit this course moved away from on 2026-11-05.

**Options.**
- **A — keep as is.** Defensible, and it teaches SQL.
- **B — Dapper with inline SQL in the query handler (recommended).** This is what Microsoft's reference
  does. Each read is still hand-written SQL, but it lives in the slice next to its handler, and a change is
  a code edit, not a migration.
- **C — EF Core projections for most reads, Dapper for reports.** This is the most common practice in
  current projects, but it unbans EF for reads, which is a bigger change to the course's story.

## 10. Cross-module transaction — Keep

**Evidence.** Sources genuinely disagree. Jovanović says to treat module boundaries as transaction
boundaries. Richardson (microservices.io) says a modular monolith operation spanning modules should usually
be one ACID transaction, and to avoid eventual consistency unless it solves a real problem. The common middle
path is to keep a shared transaction while modules share one database, and move to the outbox when a module
must split out. That's what the course does: the transaction is explicit in the handler, and reactions after
the fact already use the outbox.

**Only change.** Lesson 13 names the cost plainly: one module's transaction holds locks on another's tables,
and splitting a module out later means rewriting that flow.

## 11. Front end — Your call

**Evidence.** In .NET 8 Microsoft removed the "ASP.NET Core hosted" Blazor WebAssembly template and said to
build that scenario with the **Blazor Web App** template and the interactive WebAssembly render mode. Existing
hosted apps are still supported, and Microsoft has a migration guide.

**Options.**
- **A — keep the hand-built hosting.** It works and is supported.
- **B — move to a Blazor Web App with global `InteractiveWebAssembly` (recommended, but large).** This is the
  shape Microsoft's templates and docs now assume, including the built-in auth-state serialization. It
  touches lesson 01, lesson 06 (sign-in) and the CSP work in lesson 22.

## 12. Browser auth — Keep

The browser signs in with Identity's cookie, on the same origin as the API. Andrew Lock recommends against
storing bearer tokens in the browser, and Curity's SPA guidance prefers cookie-only browsers. The course
already matches this.

## 13. Migrations — Keep

Microsoft's docs say applying migrations at runtime "isn't ideal" for production and recommend SQL scripts
or migration bundles. The course runs migrations at startup only when `Database:MigrateOnStartup` is on
(Development and tests) and uses bundles in production. That matches.

## 14. Test framework — Change, after a spike

**Evidence.** xUnit v3 is the current line (4.0 shipped 14 August 2026), and a report in the dotnet/templating
tracker says v2 is in maintenance mode. `FsCheck.Xunit.v3` exists. Stryker.NET needs its Microsoft Testing
Platform runner for xUnit v3, which its own blog announced as a preview. This is the risk the spike checks.

**Spike result (2026-11-13): it works.** `xunit.v3` 4.0.3 + `FsCheck.Xunit.v3` 3.6.0 + AwesomeAssertions
9.6.0 ran under `dotnet test`. Stryker.NET 5.1.0 with `--test-runner mtp` found the tests, captured per-test
coverage and scored the mutants, with a "preview" warning. `coverlet.MTP` 10.1.0 (MIT) wrote a Cobertura
report via `dotnet test --coverlet`.

**What changes with it (lesson 02 must show each):**
- A `global.json` with `"test": { "runner": "Microsoft.Testing.Platform" }`. Without it the .NET 10 SDK
  refuses: "Testing with VSTest target is no longer supported".
- Test projects are executables (`<OutputType>Exe</OutputType>`), and files need `using Xunit;`.
- `coverlet.collector` → `coverlet.MTP`; `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` go.
- Stryker runs with `--test-runner mtp`, and the lesson mentions that the runner is marked preview.
- `IAsyncLifetime` returns `ValueTask` in v3, so the test fixtures change signature.

## 15. Build hygiene — Change

From my own knowledge, not a source found today: production .NET repos usually set `TreatWarningsAsErrors`
(at least in CI), `AnalysisLevel` to `latest-recommended` and `EnforceCodeStyleInBuild`, with an
`.editorconfig`. Our `Directory.Build.props` sets only the target framework, nullable and implicit usings.
This would be added in lesson 01 and enforced in CI from lesson 12.

## 16. Error codes — Add

**The idea (user, from the C# Yellow Book).** Compiler errors such as `CS0122` have a stable ID, so you can
look up what causes one and how to fix it. The ERP's errors should work the same way.

**Evidence.** RFC 9457, which our errors already follow, makes the `type` URI the problem's primary
identifier, and guides add that it should lead to human-readable documentation. Stripe, a widely copied API,
returns a short `code` plus a `doc_url` and publishes a catalog of codes. Guides agree that a published code
must never change meaning, like an API path.

**Two IDs, two jobs.** The error **code** names the *kind* of problem (every credit-limit refusal is the same
code). The **`traceId`** we already return names *one occurrence* (this refusal, at this time, in the logs).
Support asks for both: the code says what went wrong, the trace id finds it.

**Design.**
- Codes look like `SALES-0003`: module prefix plus a number, short enough to read out over the phone. Each
  module lists its codes in one internal class (`SalesErrors`), with a title, the cause and the fix. The
  number of a code is never reused.
- `DomainException` takes an `ErrorCode` instead of a bare message. `GlobalExceptionHandler` writes
  `type: "/errors/SALES-0003"` and a `code` extension member, beside the existing `traceId`.
- Generic codes cover what isn't a business rule: validation (field errors as today), not found, concurrency
  conflict (409), and unexpected (500, where the trace id matters most).
- The app serves the catalog: `/errors/{code}` is a Blazor page that shows the cause and the fix, so the
  `type` link really resolves. The UI shows "Credit limit exceeded (SALES-0003)" and links to it.
- Tests: codes are unique, every code has a title, cause and fix, and every code is thrown somewhere (no
  dead entries).

**Cost.** One catalog entry per business rule, and the discipline never to reword a code into a different
meaning. Lesson 03 introduces the mechanism; each later lesson adds its module's codes as it adds rules.

---

## Plan (after your decisions)

Code first, lessons second, per the ROADMAP rule.

1. **Spike (in a scratch copy).** These questions decide details, not direction:
   - Does `AddValidation()` reject internal models?
   - Does a FluentValidation endpoint filter work with internal validators?
   - Does `Microsoft.AspNetCore.OpenApi` describe internal request types?
   - Does Stryker's MTP runner run against xUnit v3 + FsCheck v3 here?
   - What does Microsoft's current guidance say about `TypedResults`?
2. **Reference build, in order, tests green after each step:**
   1. `TimeProvider` (small, independent).
   2. Error codes: `ErrorCode`, per-module catalogs, `DomainException(ErrorCode)`, `code` + `type` in the
      problem response (the `/errors/{code}` page comes with step 9).
   3. Domain/integration event split and the integration-event dispatcher; the outbox no longer uses Mediator.
   4. Handlers injected directly; validation filter; `MapXxxEndpoints`; Carter and Mediator removed;
      `NotFoundException` → `TypedResults`.
   5. `InternalsVisibleTo Erp.Api` removed; architecture tests tightened (including the CQRS test: no
      DbContext in a query handler, no Dapper in a command handler).
   6. OpenAPI swap.
   7. xUnit v3, if the spike passed.
   8. Build hygiene.
   9. Blazor Web App with interactive WebAssembly, plus the error catalog page.
3. **Lessons.** Rewrite lesson 03 (likely "Features, handlers and the validation filter"; the post-office
   analogy moves to the event dispatcher in lesson 13, where it fits better). Then update the
   endpoint/handler snippets in every feature lesson, lesson 13 (dispatcher, transaction cost note), lesson
   14 (telemetry without a behavior), lesson 02 (xUnit v3, `FakeTimeProvider`), GLOSSARY and ANSWER-KEY.
4. **Rules.** Update CLAUDE.md §3–5 (stack, `InternalsVisibleTo` rule, conventions), README and ROADMAP (M9),
   THIRD-PARTY-NOTICES.

---

## Sources

- Mediator README (generator placement, assembly scanning): https://github.com/martinothamar/Mediator
- Mediator issue, generated code across projects / `InternalsVisibleTo`: https://lightrun.com/answers/martinothamar-mediator-conflict-in-generated-code-if-imported-in-multiple-projects
- Refactoring a modular monolith without MediatR: https://antondevtips.com/blog/refactoring-a-modular-monolith-without-mediatr-in-dotnet
- Cross-cutting concerns in VSA (endpoint filters): https://milanjovanovic.tech/blog/cross-cutting-concerns-in-vertical-slice-architecture
- Minimal API route handlers and `MapGroup`: https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/route-handlers
- .NET 10 validation overview: https://learn.microsoft.com/aspnet/core/validation/overview
- Minimal API validation in .NET 10 (public-models caveat): https://dev.to/adrianbailador/minimal-api-validation-in-net-10-3c02
- Best practices for exceptions: https://learn.microsoft.com/dotnet/standard/exceptions/best-practices-for-exceptions
- `TimeProvider` overview: https://learn.microsoft.com/dotnet/standard/datetime/timeprovider-overview
- Replacing a homegrown `IClock` with `TimeProvider`: https://blog.maartenballiauw.be/posts/2026-07-22-timeprovider-and-the-end-of-untestable-datetime-now/
- Swashbuckle no longer in the .NET 9 template: https://timdeschryver.dev/blog/what-about-my-api-documentation-now-that-swashbuckle-is-no-longer-a-dependency-in-aspnet-9
- CQRS reads in eShopOnContainers (Dapper): https://learn.microsoft.com/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/cqrs-microservice-reads
- EF Core vs Dapper: https://milanjovanovic.tech/blog/ef-core-vs-dapper
- Saga pattern in a modular monolith (module boundary = transaction boundary): https://milanjovanovic.tech/blog/saga-pattern-modular-monolith
- Transaction management in a modular monolith (single ACID transaction): https://microservices.io/post/architecture/2023/11/13/how-modular-can-your-monolith-go-part-6-transactional-commands.html
- Domain events vs integration events: https://milanjovanovic.tech/blog/domain-events-vs-integration-events
- What's new in ASP.NET Core 8 (hosted WASM folded into Blazor Web App): https://learn.microsoft.com/aspnet/core/release-notes/aspnetcore-8.0
- Should you use the .NET 8 Identity API endpoints?: https://andrewlock.net/should-you-use-the-dotnet-8-identity-api-endpoints
- Applying EF Core migrations: https://learn.microsoft.com/ef/core/managing-schemas/migrations/applying
- xUnit v3 migration: https://xunit.net/docs/getting-started/v3/migration
- xUnit v3 4.0.0 release: https://xunit.net/releases/v3/4.0.0
- FsCheck.Xunit.v3: https://www.nuget.org/packages/FsCheck.Xunit.v3/
- Stryker.NET MTP runner: https://stryker-mutator.io/blog/stryker-net-mtp-runner/
- Minimal API responses (`TypedResults` preferred): https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/responses
- RFC 9457, Problem Details for HTTP APIs: https://www.rfc-editor.org/rfc/rfc9457.html
- Problem Details hands-on (type URI to docs): https://swagger.io/blog/problem-details-rfc9457-api-error-handling
- Stripe error codes: https://docs.stripe.com/api/errors
