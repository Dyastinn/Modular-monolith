# Phase 15b — `internal` All the Way: Shrinking a Module to Its Contract (Optional)

> 🚫 **Read [Phase 15](15-scaling-the-wall.md) first.** This phase finishes the job that one starts. It assumes
> you've already moved `IStockReservation` into `Modules/Inventory/Contracts/` and marked
> `StockReservationService` `internal`. On its own, none of what follows makes sense.

---

> 👨‍🏫 **From your senior:** Phase 15 ended on a satisfying compiler error — Sales referencing the whole
> Inventory assembly and *still* unable to touch `StockReservationService`. That error is real. It's also
> covering about one-twentieth of your codebase. We locked one class in one module and left every other type
> in the solution wide open, including all of Sales. This phase is the audit nobody enjoys and everybody needs:
> go type by type, ask "who outside this assembly is *supposed* to name you," and mark the honest answer.
> Most of the work is one keyword. The interesting part is the three places where the frameworks quietly
> disagree with you — and one of them breaks your app without a single error message.

## In one sentence

Reduce each module's public surface to exactly two things — its **contracts** and its **`Add…Module` seam** —
by marking everything else `internal`, and learn the three framework behaviours that turn that keyword into a
silent outage if you don't handle them.

## What you'll build

Starting from the Phase 15 build, you'll:

- Mark every type in the Sales module `internal` — Domain, Features, Infrastructure, all of it — leaving
  `DependencyInjection` as the module's only `public` type.
- Register Sales's Carter endpoints **by hand**, because `AddCarter()` cannot see `internal` ones.
- Pass `includeInternalTypes: true` to FluentValidation, because its scanner cannot see `internal` validators
  either — and says nothing when it finds none.
- Add `InternalsVisibleTo` for the **unit**-test assembly, alongside Phase 15's integration-test grant.
- Add a regression test that fails if the validator wiring silently breaks.

Runnable reference: [`course-validation/business-phase15/`](../course-validation/business-phase15/) — the same
build Phase 15 points at, now carrying both refactors. `dotnet test` green at **14 unit + 8 integration**.
(Phase 15 alone was 14 + 7; the eighth integration test is the validator guard you'll add in §4.)

## 1. The pain first — `public` is a promise you didn't mean to make

Phase 15 built a reception desk in front of Inventory. Here's what the rest of the building looks like.

Nothing stops anyone from opening `Erp.Api/Program.cs` and writing this today:

```csharp
using Erp.Modules.Sales.Domain;

// Somewhere in the host, at 4:50pm on a Friday.
var customer = new Customer(new CustomerId(7), "Acme", isBlocked: false, new Money(50_000), Money.Zero);
customer.EnsureWithinCreditLimit(new Money(999_999));
```

It compiles. Of course it does — `Customer` is `public`. And every rule you spent Phases 5 through 9 pushing
*into* the domain just became optional, because there is now a second way to reach a `Customer` that doesn't
go through `ICustomerRepository`, doesn't run inside a transaction, and doesn't know about the credit-limit
check the handler performs two lines later.

That's the small version. The structural version is worse:

- **Every `public` type is a promise.** Marking a type `public` says "other assemblies may depend on this, and
  I'll think about them before I change it." You never *meant* to promise that about `SalesOrderLine` or
  `SalesOrderHeaderRow` — a Dapper row-mapping DTO that exists for exactly one query. But the keyword doesn't
  know that. Rename it and you're technically breaking an API you published by accident.
- **The wall you can see isn't the wall you have.** Phase 15's lesson was "the compiler is now the wall." But a
  compiler only refuses what you've told it to refuse. With one `internal` in the whole solution, the compiler
  is enforcing one class. Everything else is still running on the honour system that Phase 15 was supposed to
  replace.
- **`Shared` was never the only junk drawer.** `Erp.Modules.Sales` currently exports about forty types. That's
  not a module with a boundary; it's a namespace with a `.csproj`.

> 💡 **Back to the reception desk.** Phase 15 put a front counter on Inventory and locked its interior doors.
> Sales, meanwhile, is a building with a reception desk and no doors at all — you can walk past the counter
> into the accounts office. Same analogy, and this phase is where you finally fit the locks.

> 🧭 **From WebForms:** `internal` is C#'s `Friend` — visible to everything in this assembly, invisible outside.
> If you ever shipped a class library and regretted a `Public` you couldn't take back, this is that lesson,
> applied to a boundary inside one solution rather than across a NuGet package.

## 2. The audit — what stays `public`

Go through a module and ask one question of each type: **does something in another assembly need to name it?**
There are exactly two honest yeses.

| Stays `public` | Why |
|---|---|
| `Contracts/IStockReservation.cs` | Another module calls it. That's the whole point of Phase 15. |
| `DependencyInjection.AddSalesModule` | The host calls it to wire the module up. |

Everything else is a no:

| Goes `internal` | Who you thought needed it | Who actually does |
|---|---|---|
| `SalesOrder`, `Customer`, `Money`, `Quantity` | "the tests" | Only the test assembly — handled by `InternalsVisibleTo`, not by `public` |
| `ISalesOrderRepository` and the other ports | "Infrastructure implements it" | Infrastructure is in the *same* assembly |
| `CreateSalesOrderCommand`, `…Handler`, `…Validator` | "the mediator resolves it" | Reflection and DI ignore accessibility entirely (§3) |
| `CreateSalesOrderEndpoint` + its `Request`/`Response` records | "Carter and Swagger need them" | Half true — this is the one that bites (§4) |
| `SalesOrderRepository`, `SalesOrderHeaderRow` | nobody, ever | nobody, ever |

> 🔑 **Ports are not contracts.** This trips people up mid-audit, so it's worth restating from
> [Phase 15 §6](15-scaling-the-wall.md#6-common-mistakes--best-practices): `ISalesOrderRepository` is a **port** —
> how the module reaches *its own* database, implemented by its *own* Infrastructure. It goes `internal`.
> `IStockReservation` is a **contract** — implemented by this module, called by a *different* one. It's
> `public`. If you're unsure which you're holding, ask who implements it.

In the reference build the change is mechanical: every `public` at the start of a line in
`src/Modules/Sales/` becomes `internal`, then `DependencyInjection` goes back to `public`. That's it —
one type public, thirty-nine internal.

## 3. What just works (and why it feels like it shouldn't)

Before the two things that break, the reassuring part — because most readers expect this to explode and it
doesn't.

**Your handlers, commands, and validators can all be `internal`.** The mediator finds them by reflection:

```csharp
var handlerTypes = assembly.GetTypes()          // GetTypes() returns internal types too
    .Where(t => !t.IsAbstract && !t.IsInterface)
    .SelectMany(t => t.GetInterfaces()
        .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
        .Select(i => new { Implementation = t, Service = i }));
foreach (var h in handlerTypes) services.AddTransient(h.Service, h.Implementation);
```

`Assembly.GetTypes()` returns every type in the assembly regardless of accessibility, and the DI container
activates types by reflection, which doesn't check whether *you* could have named them in source. An `internal`
handler implementing a `public` interface resolves and runs exactly as before. The registration itself is legal
because this code lives *inside* Sales, where `internal` is visible.

**Your endpoint DTOs can be `internal` too.** Minimal-API model binding and Swagger schema generation both work
by reflection over the type. An `internal record CreateSalesOrderRequest` binds from JSON and shows up in
`swagger.json` with a full property schema, exactly like a `public` one.

> ❓ **"Then can I make the Handler `internal` but keep `Handle` public?"** No — and you don't need to. C# caps
> a member's reach at its containing type's: a `public` method on an `internal` class is still assembly-only,
> so that combination buys you nothing. The reason it feels necessary is the assumption that *something outside
> calls `Handle` by name*. Nothing does. The mediator holds an `IRequestHandler<,>` reference and invokes
> through the interface, and interface dispatch doesn't care that the implementing type is invisible to the
> caller. Accessibility governs **what you can write in source**, not what reflection can reach at runtime.

## 4. What breaks — the two silent ones

Neither of these throws. That's what makes them worth a section.

### Carter stops finding your endpoints

`builder.Services.AddCarter()` scans your assemblies for `ICarterModule` implementations — and its scan
**only considers `public` types**. Mark `CreateSalesOrderEndpoint` `internal` and Carter simply doesn't see it.
No exception at startup. No warning in the log. The route is never mapped, so:

- `POST /sales-orders` returns **404**.
- The endpoint vanishes from Swagger entirely — which usually reads as "Swagger is broken," sending you off to
  debug the wrong component for an hour.

⚠️ **This is the failure most people hit and misdiagnose.** Swagger isn't broken. Swagger is faithfully
reporting a route table that no longer contains your endpoint.

The fix is the same trick that made the DI registration legal: do the work *inside* the module's assembly,
where `internal` is visible. Add this to `AddSalesModule`:

```csharp
// src/Modules/Sales/DependencyInjection.cs
using Carter;

// Register every ICarterModule endpoint defined in THIS module. Carter's own AddCarter() scan only picks up
// PUBLIC types, and every endpoint here is now `internal` — so AddCarter() sees none of them and the routes
// silently vanish. We register them ourselves, from inside the assembly where `internal` IS visible.
var endpointTypes = assembly.GetTypes()
    .Where(t => !t.IsAbstract && !t.IsInterface && typeof(ICarterModule).IsAssignableFrom(t));
foreach (var e in endpointTypes) services.AddSingleton(typeof(ICarterModule), e);
```

`app.MapCarter()` resolves `ICarterModule` from the container, so anything you register there gets mapped
regardless of how it was discovered. Same reflection loop you already use for handlers, one line of filter
different.

### FluentValidation stops finding your validators

This one is worse, because the app keeps serving traffic.

`AddValidatorsFromAssembly(assembly)` also skips `internal` types by default. It doesn't throw when it finds
none — it registers nothing and returns happily. Your pipeline then resolves no `IValidator<T>`, skips
validation, and passes the unvalidated command straight to the handler.

So a `POST /sales-orders` with **zero lines** — the exact case `CreateSalesOrderValidator` exists to reject —
stops returning `400` and starts returning `201 Created`, having written an empty order to your database.

The fix is one argument:

```csharp
// includeInternalTypes: true is REQUIRED now the validators are internal. Without it FluentValidation finds
// nothing, registers nothing, and throws no error — validation just silently stops running.
services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
```

⚠️ **A green test suite will not catch this for you.** In the reference build, reverting that one argument
leaves all 21 of the Phase 15 tests passing — none of them submits a malformed command. A bug that your tests
are structurally blind to is exactly the kind worth pinning down with a test of its own:

```csharp
// tests/Erp.Integration.Tests/SalesOrderWorkflowTests.cs
// Regression guard: the validators are `internal`, and FluentValidation's assembly scanner SKIPS internal
// types unless AddSalesModule passes `includeInternalTypes: true`. Get that wrong and nothing throws —
// validation just silently stops running, and this order (zero lines) sails past CreateSalesOrderValidator
// into the handler and is happily persisted as 201 Created.
[Fact]
public async Task Order_with_no_lines_is_rejected_by_the_internal_validator()
{
    var client = await LoggedInAs("manager");
    var create = await client.PostAsJsonAsync("/sales-orders",
        new { customerId = 1, lines = Array.Empty<object>() });
    create.StatusCode.Should().Be(HttpStatusCode.BadRequest);   // 400, not 201 — the validator ran
}
```

That's the eighth integration test, and it's the reason this phase's checkpoint says 8 rather than 7.

> 🔑 **The pattern behind both bugs.** A framework that discovers your types by scanning has an opinion about
> accessibility; a framework that *activates* types you handed it does not. Registration-by-scan (Carter's
> `AddCarter`, FluentValidation's `AddValidatorsFrom…`) filters on `public`. Activation-by-reflection (the DI
> container, minimal-API binding, Swagger schema generation) doesn't care. When you internalise a module, you
> take over the scanning yourself — and you only have to, for the frameworks that scan.

## 5. The guest list, per module

Phase 15 added one `InternalsVisibleTo` so the concurrency test could reach Inventory's internals. Now the
Sales **unit** tests need the same courtesy — `DomainTests.cs` news up `Customer`, `Money` and `SalesOrder`
directly, and all three just went `internal`:

```xml
<!-- src/Modules/Sales/Erp.Modules.Sales.csproj -->
<ItemGroup>
  <InternalsVisibleTo Include="Erp.Modules.Sales.UnitTests" />
</ItemGroup>
```

```xml
<!-- src/Modules/Inventory/Erp.Modules.Inventory.csproj — from Phase 15, unchanged -->
<ItemGroup>
  <InternalsVisibleTo Include="Erp.Integration.Tests" />
</ItemGroup>
```

Two modules, two different guest lists, each naming only what it actually needs. Inventory doesn't trust the
Sales unit tests; Sales doesn't trust the integration tests. That asymmetry is the point — a guest list that
ends up identical everywhere has stopped being a decision.

> ⚠️ **The one entry that undoes everything** is another *module*. `<InternalsVisibleTo Include="Erp.Modules.Sales" />`
> on Inventory hands Sales the entire assembly and puts you back where §1 started, except now it looks
> deliberate. Test assemblies: yes. Modules: never.

## 6. A request through a fully-internalised module

`POST /sales-orders`, with exactly one `public` type in the whole Sales assembly:

1. **Startup** — the host calls `AddSalesModule`. Inside Sales's own assembly, the reflection loops register
   the `internal` handlers, the `internal` endpoints as `ICarterModule`, and the `internal` validators (with
   `includeInternalTypes: true`). Every one of those registrations is legal because the code doing it lives
   *in* the assembly it's scanning.
2. **`app.MapCarter()`** — resolves `IEnumerable<ICarterModule>` from DI, finds the endpoints we registered by
   hand, and calls `AddRoutes` on each. `/sales-orders` is now mapped.
3. **Request arrives** — minimal-API binding deserialises the JSON body into `internal record
   CreateSalesOrderRequest` by reflection. Accessibility never enters into it.
4. **`sender.Send(...)`** — the mediator resolves `IRequestHandler<CreateSalesOrderCommand, long>` from DI and
   gets back an `internal CreateSalesOrderHandler`, held through the `public` interface.
5. **Validation** — `IValidator<CreateSalesOrderCommand>` resolves to the `internal` validator and runs.
6. **The domain** — the handler calls `customer.EnsureNotBlocked()`, `order.AddLine(...)`,
   `customer.EnsureWithinCreditLimit(...)` on `internal` types, from inside the assembly that defines them.
7. **The one reach outward** — `IStockReservation`, the `public` contract from Phase 15, on the submit path.

The request behaves identically to Phase 14's. What changed is what a *developer* can write: the host can call
`AddSalesModule` and nothing else, and Inventory can call `IStockReservation` and nothing else.

## 7. Why each component exists

| Piece | Why it's there |
|---|---|
| `internal` on every Sales type but one | Shrinks the module's published surface from ~40 types to 1. What isn't public can't be depended on, so it can be renamed, split, or deleted without consulting anyone. |
| `public static class DependencyInjection` | The composition seam. The host must be able to call `AddSalesModule` — this is the second of the two legitimate public surfaces. |
| The `ICarterModule` registration loop | Replaces `AddCarter()`'s public-only scan for this module's endpoints. Without it the routes vanish silently and Swagger looks broken. |
| `includeInternalTypes: true` | Replaces FluentValidation's public-only scan. Without it validation silently stops running while the app keeps returning success. |
| `Order_with_no_lines_is_rejected_by_the_internal_validator` | Pins down the failure the other 21 tests can't see. |
| `InternalsVisibleTo` per module | The deliberate, named holes — test assemblies only, decided separately for each module. |

## 8. Common mistakes & best practices

- ❌ **Marking things `internal` and shipping because it compiles.** Both bugs in §4 compile perfectly. ✅ Run
  the app and hit a route; check Swagger lists what it did before; submit one invalid payload.
- ❌ **Making an endpoint `public` again "because Swagger needs it."** Swagger never needed it — Carter's
  scanner did. ✅ Register the endpoint yourself and keep it `internal`.
- ❌ **Reaching for `public` the moment a test won't compile.** ✅ `InternalsVisibleTo` with the test
  assembly's name. `public` opens the type to *every* assembly to satisfy one.
- ❌ **Internalising the `Add…Module` seam.** The host can no longer wire the module up, and the error is a
  confusing one. ✅ Contracts and the seam stay `public`; that's the whole allow-list.
- ❌ **Doing all modules in one weekend.** ✅ One module at a time, tests green between each. The framework
  gotchas are per-module and you want to meet them one at a time.
- ✅ **Treat a new `public` as a code-review question.** Once a module is down to one public type, any PR that
  adds a second is asking to publish something. Sometimes the answer is yes — it should be a conversation.

## 9. Checkpoint — one public type, and everything still works

From [`course-validation/business-phase15/`](../course-validation/business-phase15/):

```
dotnet build      # 0 errors
dotnet test       # 22 passing (14 unit + 8 integration)
```

Then run the app and confirm the two silent failures aren't happening:

```
GET  /swagger/v1/swagger.json     # /sales-orders is still listed — Carter found the internal endpoints
POST /sales-orders  {"customerId":1,"lines":[]}    # 400, not 201 — the internal validator still runs
```

Now prove the wall, the way Phase 15 did — by getting refused. Drop this into `Erp.Api/Program.cs` and build:

```csharp
var c = new Erp.Modules.Sales.Domain.Customer(default, "x", false, default, default);
var e = new Erp.Modules.Sales.Features.CreateSalesOrderEndpoint();
```

```
error CS0122: 'Customer' is inaccessible due to its protection level
error CS0122: 'CreateSalesOrderEndpoint' is inaccessible due to its protection level
```

The host — which *references Sales directly and calls into it every request* — cannot name a single type in it
beyond `AddSalesModule`. Compare that with §1, where the same host could construct a `Customer` and skip every
rule in the domain. Delete the probe and you're done.

---

## ✅ Check Yourself

> Try each one before you look. [Model answers](ANSWER-KEY.md#phase-15b-internal-all-the-way) — but attempting first is what makes them stick.

1. A module has exactly two kinds of `public` type. What are they, and why is everything else `internal`?
2. You mark your endpoints `internal` and `/sales-orders` starts returning 404 while Swagger stops listing it.
   What actually broke — and why is "Swagger is broken" the wrong diagnosis?
3. Why can an `internal` handler still be resolved and invoked by the mediator, when Sales code in another
   assembly couldn't even name the type?
4. Why won't `internal class` + `public Handle()` give you the "internal type, public method" you wanted?
5. Which of the two §4 failures is more dangerous in production, and why?
6. Your full test suite is green after internalising a module. Name a bug that suite cannot see, and the test
   that would catch it.
7. `ISalesOrderRepository` and `IStockReservation` are both interfaces a module declares. One is `public` and
   one is `internal` — which is which, and what's the question that tells them apart?

---

← Previous: [Phase 15 — Scaling the Wall](15-scaling-the-wall.md) · [Tutorial overview](00-START-HERE.md) · **This is the last phase — 🎉 you've gone all the way through.**
