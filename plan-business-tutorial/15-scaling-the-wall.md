# Phase 15 — Scaling the Wall: Per-Module Contracts with `internal` (Optional)

> 🚫 **This phase changes the architecture — that makes it different from every phase before it, including
> Phase 14.** Phase 14's whole rule was "hardening wraps *around* the build, it never rewrites it." This one
> rewrites something the base course taught you: *where a cross-module contract lives, and what actually
> enforces the wall.* So it isn't a bug-fix or a "you missed this" — the base build is correct as it stands.
> It's the refactor you reach for **when one shared folder stops scaling**, and not a day before.

---

> 👨‍🏫 **From your senior:** Back in [Phase 7](07-submit-reserve-concurrency.md) we put `IStockReservation` in
> `Shared`, and I told you the wall between Sales and Inventory is "the absent project reference" — Sales
> can't call into Inventory because its `.csproj` has no line pointing at Inventory. That's true, it's simple,
> and it's exactly the right thing to teach first. It also quietly stops scaling around the sixth or seventh
> module. This phase is what you do about that. If you only ever have two or three modules, you may never need
> it — that's fine. Read it so you recognise the pain when it arrives.

## In one sentence

Move each module's cross-module contract **out of the shared kernel and into the module that owns it**, make
everything else in that module `internal`, and let the **compiler** enforce the wall — so a consumer can
reference the whole module and still touch nothing but the published contract.

## What you'll build

You'll take the exact Sales ↔ Inventory relationship you already have and re-cut the boundary:

- `IStockReservation` moves from `Shared/Erp.Shared.Common/Contracts/` into
  `Modules/Inventory/Contracts/` — Inventory now **owns** its contract.
- `StockReservationService` becomes `internal`.
- `Sales` gains a project reference to the **Inventory assembly** — and, because of `internal`, can still
  see only the contract.
- The integration test that reaches Inventory's internals gets an explicit `InternalsVisibleTo`.

Runnable reference for this whole phase:
[`course-validation/business-phase15/`](../course-validation/business-phase15/) — a copy of the base build
with exactly these changes, `dotnet test` green (14 unit + 7 integration). The base build
[`course-validation/business/`](../course-validation/business/) is left alone, because Phases 0–14 still
describe *it*.

## 1. The pain first — why `Shared` stops scaling

Here's the shared kernel at the size this course leaves it:

```
Shared/Erp.Shared.Common/
└── Contracts/
    └── IStockReservation.cs      # Inventory's promise to the world
```

One file. Obvious. Now imagine the ERP grew the way real ones do — Purchasing, Shipping, Billing, Pricing,
Tax, Notifications — and every cross-module promise landed in the same place, because that's the rule you
were taught:

```
Shared/Erp.Shared.Common/
└── Contracts/
    ├── IStockReservation.cs      # owned by... Inventory? you have to go check
    ├── IPurchaseOrderClosal.cs   # owned by Purchasing
    ├── ITaxQuote.cs              # owned by Tax
    ├── IShipmentBooking.cs       # owned by Shipping
    ├── ICreditHold.cs            # owned by Billing
    ├── IPriceList.cs             # owned by Pricing
    └── ... eleven more ...
```

Three things have quietly gone wrong, and none of them is about any single interface:

- **Ownership is a guess.** Who's allowed to change `ITaxQuote`? Nothing in the folder says. The interface
  lives next to the fifteen others in a project no module *owns*, so "whoever edits it last" becomes the de
  facto owner. That's how a shared kernel rots into a junk drawer — the thing [Phase 14](14-going-further.md)
  warned about for Redis, happening to your *architecture* instead of your infrastructure.
- **`Shared` is now a bottleneck.** Every module references `Erp.Shared.Common`, so a change to *any* contract
  touches the assembly *every* module depends on. A one-line edit to `IShipmentBooking` recompiles the world
  and drags fifteen teams into one merge queue.
- **The wall got weaker exactly when you needed it strong.** The base course's wall was "no project
  reference." But contracts-in-`Shared` only *works* because every module references `Shared`. The moment a
  contract needs a type that lives in its owning module — an enum, a value object, a result shape — you can't
  put it in neutral `Shared` without dragging that type in too, and now `Shared` knows about Inventory's
  innards. The neutral kernel stops being neutral.

> 💡 **The junk-drawer test.** Open your `Shared/Contracts` folder and point at any interface. If you can't
> say *which module owns it* without opening the file, the kernel has started rotting. One or two contracts:
> fine, leave it. Fifteen you can't attribute at a glance: this phase.

## 2. Why the base course used `Shared` anyway (and why that was right)

Before we fix it, be clear that the base design wasn't a mistake to apologise for.

Contracts-in-`Shared` buys you a wall you can **see in five seconds**: open `Erp.Modules.Sales.csproj`,
notice there's no line mentioning Inventory, and you *know* Sales cannot call Inventory. No access modifiers,
no assembly visibility rules, no test-project ceremony. For a reader meeting modular monoliths for the first
time, "the missing reference is the wall" is the single clearest way to make the boundary *tangible*. You
don't teach `internal`-as-architecture on day one; you teach it when the simple wall starts to chafe.

> 🔑 **Both designs are legitimate.** This isn't "the base course was wrong, here's right." It's "the simple
> wall is right until you have enough modules that ownership and blast-radius start to hurt — then you trade
> up." Adopt the trade when you feel the pain in §1, the same way you adopted the mediator only after the
> hand-wired `switch` hurt.

## 3. The fix — a module owns its contract, `internal` guards the rest

The move has two halves, and they only work together.

**Half one: the contract moves home.** `IStockReservation` leaves `Shared` and becomes a `public` type in the
Inventory module, in its own `Contracts` folder:

```csharp
// src/Modules/Inventory/Contracts/IStockReservation.cs
using System.Data;

namespace Erp.Modules.Inventory.Contracts;

// Inventory's ONE public type: the contract Sales calls to reserve/release stock (relocated here in Phase 15).
public interface IStockReservation
{
    Task<bool> TryReserveAsync(long itemId, int qty, IDbTransaction tx, CancellationToken ct);
    Task ReleaseAsync(long itemId, int qty, IDbTransaction tx, CancellationToken ct);
}
```

Now Sales needs to *reach* that type, which means — for the first time in this course — **one module
references another**:

```xml
<!-- src/Modules/Sales/Erp.Modules.Sales.csproj -->
<ItemGroup>
  <ProjectReference Include="..\Inventory\Erp.Modules.Inventory.csproj" />
</ItemGroup>
```

Read that line with the base course in your head and it should alarm you: *we just tore a hole in the wall.*
Sales can now see Inventory. That's what half two is for.

**Half two: everything that isn't the contract goes `internal`.** The implementation Sales must never touch:

```csharp
// src/Modules/Inventory/Infrastructure/StockReservationService.cs
internal sealed class StockReservationService : IStockReservation   // was: public
{
    // ...unchanged: the atomic proc call...
}
```

`internal` means "visible only inside *this* assembly." So even though Sales references the whole Inventory
project, the only Inventory type it can name is the one marked `public` — the contract. The wall didn't come
down; it **changed material**. It used to be the absent reference. Now it's the compiler.

> 💡 **From "different building" to "reception desk."** The base course's wall was the *island* — Sales and
> Inventory on separate landmasses with no bridge (no reference). This phase builds a bridge, but lands it at
> Inventory's **reception desk** (`public` contract) and locks every interior door (`internal`). You can walk
> in; you can only reach the front counter. Same guarantee, enforced one layer lower.

Registering an `internal` implementation against a `public` interface is completely fine — the registration
runs *inside* Inventory's assembly, where `internal` is visible:

```csharp
// src/Modules/Inventory/DependencyInjection.cs — public seam, internal wiring
public static IServiceCollection AddInventoryModule(this IServiceCollection services, IConfiguration config)
{
    services.AddScoped<IStockReservation, StockReservationService>();   // public ← internal: legal here
    return services;
}
```

And Sales's handler doesn't change in shape at all — only the `using` moves from the shared kernel to the
owning module:

```csharp
// src/Modules/Sales/Features/SubmitSalesOrder/SubmitSalesOrderHandler.cs
using Erp.Modules.Inventory.Contracts;   // was: the contract came in via Erp.Shared.Common

public sealed class SubmitSalesOrderHandler(
    ISalesOrderRepository orders,
    ICustomerRepository customers,
    IStockReservation stock,        // ← still just the contract; Inventory's internals stay invisible
    IDbConnectionFactory factory) : ICommandHandler<SubmitSalesOrderCommand, Unit>
```

## 4. The catch you must handle — `InternalsVisibleTo`

The moment you type `internal`, something breaks — and it's the *right* something to break, because it proves
the wall works. Your reservation-race integration test from [Phase 12](12-testing.md) news-up the real service
directly:

```csharp
// tests/Erp.Integration.Tests/InventoryReservationTests.cs
var service = new StockReservationService();   // ← now: 'inaccessible due to its protection level'
```

The test lives in a *different* assembly (`Erp.Integration.Tests`), so `internal` hides the service from it
too. You don't fix this by making the service `public` again — that would defeat the whole phase. You grant
**one named, trusted assembly** access to your internals:

```xml
<!-- src/Modules/Inventory/Erp.Modules.Inventory.csproj -->
<ItemGroup>
  <InternalsVisibleTo Include="Erp.Integration.Tests" />
</ItemGroup>
```

> 🔑 **`InternalsVisibleTo` is a guest list, not an open door.** You name exactly which assemblies you trust
> with your internals — here, your own test project, and nothing else. Sales is *not* on the list, so the
> production wall stays intact; only the test you wrote gets the key. If you find yourself adding another
> *module* to a module's `InternalsVisibleTo`, stop — that's the junk drawer coming back in through the side
> entrance.

## 5. Why each component exists

| Piece | Why it's there |
|---|---|
| `Modules/Inventory/Contracts/IStockReservation.cs` (`public`) | The module's entire public surface — its promise to the rest of the app. Lives *in* the owner, so ownership is unambiguous and a change recompiles only Inventory's dependents, not the world. |
| `StockReservationService` marked `internal` | The wall. Referencing the Inventory assembly no longer leaks its implementation; only the contract is reachable. |
| `Sales → Inventory` project reference | How the consumer reaches the published contract now that it isn't in neutral `Shared`. Safe *only because* everything but the contract is `internal`. |
| `InternalsVisibleTo("Erp.Integration.Tests")` | The one deliberate hole: your trusted test assembly, named explicitly, so the concurrency test can drive the real implementation. |
| `AddInventoryModule` stays `public` | A module has exactly two kinds of public surface: its **contracts** (what other modules call) and its **composition seam** (the one `Add…Module` the host calls to wire it). Everything else is `internal`. |

## 6. Common mistakes & best practices

- ❌ **Making the whole implementation `public` "so the test compiles."** That's undoing the phase to dodge
  one attribute. ✅ Add `InternalsVisibleTo` for the *named* test assembly and leave the class `internal`.
- ❌ **Creating a cycle.** If Sales references Inventory for `IStockReservation`, Inventory must **never**
  reference Sales — the compiler will reject the cycle, and rightly. ✅ Keep contract references one-way. If
  two modules genuinely need each other's contracts, that's the signal to pull the contracts into a small,
  dependency-free `Xxx.Contracts` project of their own (the next step past this phase) — never to let two
  implementation assemblies reference each other.
- ❌ **Marking domain entities `internal` and expecting your unit tests to still see them.** They won't,
  across the assembly boundary. ✅ Either keep the tested domain types `public` (they're the module's *own*
  surface, not a cross-module leak) or add `InternalsVisibleTo` for the unit-test assembly too — a deliberate
  choice, per module.
- ❌ **Confusing a *port* with a *contract*.** The repository ports from [Phase 6](06-create-order.md)
  (`ISalesOrderRepository`) look like interfaces you might "publish" too. Don't. A port is how a module
  reaches *its own* database; it stays `internal`. Only a **cross-module contract** goes in `Contracts/` and
  `public`. If you're unsure which one you're holding, ask who *implements* it: your own Infrastructure (port)
  or a different module (contract).
- ✅ **Move contracts lazily, one at a time, when a module earns it** — not in a big-bang "internalise
  everything" weekend. The pain is per-module; so is the fix.

## 7. Checkpoint — prove the wall changed material, not strength

From [`course-validation/business-phase15/`](../course-validation/business-phase15/):

```
dotnet build      # 0 errors — the Sales→Inventory reference + internal impl compile
dotnet test       # 21 passing (14 unit + 7 integration) — InternalsVisibleTo lets the race test run
```

Then prove the wall itself, the way a compiler proves things — by refusing. Drop this into any Sales file and
build:

```csharp
// anywhere in Erp.Modules.Sales
var s = new Erp.Modules.Inventory.Infrastructure.StockReservationService();
```

```
error CS0122: 'StockReservationService' is inaccessible due to its protection level
```

Sales references the *entire* Inventory assembly and **still can't touch the implementation.** That error is
the whole phase in one line: the wall is now the compiler. Delete the probe and you're done.

---

## ✅ Check Yourself

1. In the base course, what *is* the wall between Sales and Inventory — and what replaces it in this phase?
2. Why does adding a `Sales → Inventory` project reference **not** breach the wall here, when it obviously
   would in the base build?
3. You mark a service `internal` and an integration test stops compiling. What's the fix, and why is making
   the service `public` the *wrong* one?
4. A teammate adds `<InternalsVisibleTo Include="Erp.Modules.Sales" />` to Inventory so a Sales class can call
   a helper directly. What did they just undo?
5. Two modules each need the other's contract. Why can't you solve it by having both implementation assemblies
   reference each other, and what do you do instead?
6. When is contracts-in-`Shared` (the base design) the *better* choice, and how do you know you've outgrown it?

---

← Previous: [Phase 14 — Going Further](14-going-further.md) · [Tutorial overview](00-START-HERE.md) · **This is the last, and most optional, phase — 🎉 you've gone all the way through.**
