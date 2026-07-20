# Phase 1 — Solution Setup: The Skeleton for Our Modular Monolith

> Everything you need to build the solution is in this phase — the exact `dotnet` commands included. You
> never have to leave this tutorial to get set up.

---

> ⏱️ **Time for this phase:** ~1 hr. It's mostly scaffolding — `dotnet new`, project references, one
> package, a health endpoint — no business logic yet. Slower the first time you wire up Central Package
> Management.

## 1. What we're building

An empty-but-running **modular monolith** solution: a `Shared` kernel, a `Sales` module, and a health-check
endpoint that proves the app starts. **No business logic yet** — we're framing the house before decorating
rooms. We install packages and add configuration **only when a phase actually needs them**, so nothing here
is unused clutter.

By the end you can **run the app and hit `GET /health`** and get `"healthy"` back — the projects compile
with the module walls already in place. That's your first "this works" checkpoint.

> 🧭 **From WebForms:** in a WebForms app, "the solution" was usually one Web Application project with a pile
> of `.aspx` pages and a `BLL`/`DAL` folder. Here the top-level split is by **business capability** (a
> `Sales` module), and inside a module by **feature**. There is no `BLL`/`DAL` layer, and there is no
> Clean-Architecture `Domain`/`Application`/`Infrastructure` split either — those scatter one feature across
> many projects. We organize by *what the code is about*, not *what technical kind it is*.

## 2. Why it's designed this way

We use a **modular monolith with Vertical Slice Architecture** — organized by *what the code is about*
(a `Sales` module, then a feature inside it), not by technical layer. Two rules replace the old four-layer
`Domain`/`Application`/`Infrastructure`/`Api` diagram:

1. **Modules are islands** — `Sales` and (later) `Inventory` are separate projects that never reference each
   other's internals. They talk through a published contract in `Shared`.
2. **Keep the Domain pure** — inside a module, `Features/` and `Infrastructure/` use `Domain/`; `Domain/`
   uses only `Shared.BuildingBlocks`. Same testability as Clean Architecture's inner circle, without the
   ceremony.

## 3. The projects and folder structure

Here's the shape we're about to create. The `Features/` subfolders are a **preview** — we don't create them
now; each one is added in the phase that builds that feature. Everything else in the tree we scaffold in
this phase:

```
Erp.slnx
├── src/
│   ├── Shared/
│   │   ├── Erp.Shared.BuildingBlocks/   # everything a module is allowed to see. Folders, not projects:
│   │   │   ├── Domain/                  #   Entity, AggregateRoot, IDomainEvent, DomainException
│   │   │   ├── Abstractions/            #   IClock, PagedResult, and the interfaces modules depend on:
│   │   │   │                            #     IDbConnectionFactory, ICurrentUser, IStockReservation
│   │   │   └── Messaging/               #   IRequest, ICommand, IQuery, IRequestHandler, ISender, behaviors
│   │   └── Erp.Shared.Infrastructure/   # the implementations: SqlConnectionFactory, JWT/password services,
│   │                                    #   CurrentUser, AddSharedInfrastructure()
│   │
│   ├── Modules/
│   │   └── Sales/  (Erp.Modules.Sales)
│   │       ├── Domain/                  # SalesOrder aggregate, Customer, Item, value objects, events
│   │       ├── Features/                # one folder per feature (two files each)
│   │       │   ├── CreateSalesOrder/    #   CreateSalesOrderHandler.cs + CreateSalesOrderEndpoint.cs (Phase 6)
│   │       │   ├── SubmitSalesOrder/    #   (Phase 7)
│   │       │   ├── ApproveSalesOrder/   #   (Phase 8)
│   │       │   ├── CancelSalesOrder/    #   (Phase 8)
│   │       │   ├── UpdateSalesOrder/    #   (Phase 9)
│   │       │   ├── GetSalesOrder/       #   (Phase 10)
│   │       │   └── SearchSalesOrders/   #   (Phase 10)
│   │       ├── Infrastructure/          # Dapper repositories + Sql/ scripts
│   │       └── DependencyInjection.cs   # AddSalesModule(): registers this module's handlers/validators/repos
│   │
│   └── Erp.Api/
│       ├── Program.cs                   # composition root — starts the app; the module wiring grows here as we go
│       ├── appsettings.json
│       └── appsettings.Development.json
│
└── tests/
    ├── Erp.Modules.Sales.UnitTests/
    └── Erp.Integration.Tests/
```

Each module keeps its SQL scripts in its own `Infrastructure/Sql/` folder (the `sales` schema). There is no
central `db/` folder — data ownership follows the module. (A second module, `Inventory`, joins later; when
it does, it brings its own `inventory` schema the same way.)

### Create the projects (the actual commands)

Open a terminal in an empty folder and run these. `classlib` = a class library (holds code, doesn't run on
its own); `web` = a runnable web app (that's our host).

```bash
# 1) The solution file (--format slnx gives the newer XML "Erp.slnx"; omit it for a classic "Erp.sln")
dotnet new sln -n Erp --format slnx

# 2) Shared code — two class libraries (why only two: see §3b below)
dotnet new classlib -n Erp.Shared.BuildingBlocks -o src/Shared/Erp.Shared.BuildingBlocks
dotnet new classlib -n Erp.Shared.Infrastructure -o src/Shared/Erp.Shared.Infrastructure

# The BuildingBlocks folders — plain directories inside one project, not separate projects
mkdir src/Shared/Erp.Shared.BuildingBlocks/Domain
mkdir src/Shared/Erp.Shared.BuildingBlocks/Abstractions
mkdir src/Shared/Erp.Shared.BuildingBlocks/Messaging

# 3) The Sales module (a class library) and the API host (runnable)
dotnet new classlib -n Erp.Modules.Sales -o src/Modules/Sales
dotnet new web      -n Erp.Api           -o src/Erp.Api

# 4) Add every project to the solution
dotnet sln add (Get-ChildItem -Recurse *.csproj)     # PowerShell
# bash:  dotnet sln add $(find . -name "*.csproj")
```

> We create the two `tests/` projects and the `Inventory` module later, in the phases that first need them —
> not now.

Now set **which project may reference which** with `dotnet add reference`. A reference is one project saying
"I'm allowed to use that project's code"; without it, the compiler pretends the other project doesn't exist
— which is exactly how the module walls get *enforced*.

```bash
# The Sales module may use the shared building blocks — but NOT Erp.Shared.Infrastructure (see below):
dotnet add src/Modules/Sales/Erp.Modules.Sales.csproj reference src/Shared/Erp.Shared.BuildingBlocks/Erp.Shared.BuildingBlocks.csproj

# The host wires the module + shared infrastructure:
dotnet add src/Erp.Api/Erp.Api.csproj reference src/Modules/Sales/Erp.Modules.Sales.csproj src/Shared/Erp.Shared.Infrastructure/Erp.Shared.Infrastructure.csproj

# The implementations need the interfaces they implement:
dotnet add src/Shared/Erp.Shared.Infrastructure/Erp.Shared.Infrastructure.csproj reference src/Shared/Erp.Shared.BuildingBlocks/Erp.Shared.BuildingBlocks.csproj
```

That's the whole graph. Four arrows:

```
Erp.Api                   → Erp.Modules.Sales, Erp.Shared.Infrastructure
Erp.Modules.Sales         → Erp.Shared.BuildingBlocks
Erp.Shared.Infrastructure → Erp.Shared.BuildingBlocks
Erp.Shared.BuildingBlocks → (nothing)
```

**No module references another module** — that missing reference *is* the island wall, enforced by the
compiler.

Now look at the second and third lines together, because the arrow that *isn't* there is the important one:
**`Erp.Modules.Sales` does not reference `Erp.Shared.Infrastructure`.** That's the same trick as the module
wall. The module writes its Dapper repositories against the **interface** `IDbConnectionFactory`, which lives
in `Erp.Shared.BuildingBlocks` — a project that depends on nothing. The *implementation*
(`SqlConnectionFactory`, plus the JWT and password services and the `HttpContext`-reading `CurrentUser`) lives
in `Erp.Shared.Infrastructure`, and **only the host references that**. So a module physically cannot reach for
a connection string, a signing key, or `HttpContext`: those types aren't on its compile path at all.
Interfaces down in `BuildingBlocks`, implementations up in `Infrastructure`, and the host is the only place
the two are introduced to each other.

### 3b. Why two projects and not five

❓ **You might be wondering:** there are three very different kinds of thing inside `BuildingBlocks` — base
domain types, interfaces, and the mediator. Why aren't those three projects?

Because a separate project buys you exactly one thing: **the compiler stops someone from writing a
`using`.** That's worth paying for when somebody would otherwise write the wrong `using` — which is precisely
the `Infrastructure` case above. It's worth nothing when every consumer references all the pieces anyway.
`Erp.Modules.Sales` needs `Entity<TId>`, needs `IDbConnectionFactory`, and needs `ICommandHandler<,>`. Split
those into three projects and Sales just references all three; nobody is on the wrong side of any wall, so no
wall is doing any work. You'd have bought three csproj files, three sets of `using` lines, and a
dependency diagram to explain — in exchange for nothing enforced.

🔑 **The rule: a project boundary exists to make something a compile error. Folders are for organising code
you're allowed to use.** That's the same rule the modules follow — `Features/` and `Domain/` are folders
inside `Erp.Modules.Sales`, not projects, because Sales code is allowed to use all of it.

⚠️ **Be honest about what this gives up.** Because `Domain/` and `Messaging/` now live in one assembly,
nothing *stops* a domain entity from writing `using` and grabbing `ISender`. That would be bad design — the
domain shouldn't know a mediator exists — but it will compile. The old four-project layout didn't actually
prevent it either (`Erp.Modules.Sales` referenced `Messaging` regardless), so nothing was lost here; the rule
was always a convention. Phase 13 revisits how you'd enforce conventions like this with an architecture test
once the codebase is big enough to need it.

## 4. Why each folder exists (no mystery folders)

| Folder | Why it exists | Why *here* |
|---|---|---|
| `Shared.BuildingBlocks` | Everything a module may use, in three folders (below). | One project because a module needs all of it — see §3b. Depends on nothing. |
| `Shared.BuildingBlocks/Domain` | `Entity`, `AggregateRoot`, `IDomainEvent`, `DomainException`. | Generic scaffolding every module's domain reuses. |
| `Shared.BuildingBlocks/Abstractions` | `IClock`, `PagedResult`, and the **interfaces** modules code against: `IDbConnectionFactory`, `ICurrentUser`, and cross-module contracts like `IStockReservation`. | Pure, dependency-free types both modules may depend on without depending on each other. |
| `Shared.BuildingBlocks/Messaging` | The hand-rolled mediator contracts + `Sender` + behaviors. | Every module sends/handles through one mediator. |
| `Shared.Infrastructure` | The **implementations** of those interfaces: `SqlConnectionFactory`, the JWT and password services, `CurrentUser` (which reads `HttpContext`). | Framework/crypto/db plumbing. **Only `Erp.Api` references it** — no module does, so no module *can* touch it. |
| `Sales/Domain` | The `SalesOrder` that knows how to submit/approve itself; `Customer`, `Item`. | Pure business objects; depend only on `BuildingBlocks`. |
| `Sales/Features/<Feature>` | One folder per feature, **two files** (handler + endpoint). | Everything for "create a sales order" together, not scattered by technical kind. |
| `Sales/Infrastructure` | Dapper repositories and the module's `Sql/` scripts. | This module's data access; owned, not shared. |
| `Sales/DependencyInjection.cs` | `AddSalesModule()` — the module's single registration seam. | The one public way the host plugs the module in. |

*(Authentication gets its own home under the host later, when we add login in Phase 4 — the business modules
stay auth-agnostic. We don't create that folder now.)*

## 4b. Fill in the building blocks (four small types the whole course leans on)

`Erp.Shared.BuildingBlocks` is an empty class library right now. Four types go in it, and every module's
domain will inherit from or throw them. They're short, so we write all four in one file — and because they
depend on nothing, this project stays the bottom of the dependency graph.

🔑 **One convention to note now, because it holds for the whole project:** every file in
`Erp.Shared.BuildingBlocks` declares `namespace Erp.Shared.BuildingBlocks`, *regardless of which folder it
sits in*. `Domain/`, `Abstractions/`, and `Messaging/` are there so you can find things — they are not part
of the namespace. C# doesn't require namespaces to match folders, and here we deliberately keep it flat so
that a module needs exactly one `using Erp.Shared.BuildingBlocks;` to get the base types, the interfaces, and
the mediator. One `using` instead of three, in every handler you'll write.

Delete the generated `Class1.cs` and create `Domain/BuildingBlocks.cs`:

```csharp
// src/Shared/Erp.Shared.BuildingBlocks/Domain/BuildingBlocks.cs
namespace Erp.Shared.BuildingBlocks;

public abstract class Entity<TId> where TId : struct
{
    public TId Id { get; protected set; }
}

public abstract class AggregateRoot<TId> : Entity<TId> where TId : struct
{
    private readonly List<IDomainEvent> _events = new();
    public IReadOnlyList<IDomainEvent> DomainEvents => _events;
    protected void Raise(IDomainEvent e) => _events.Add(e);
    public void ClearEvents() => _events.Clear();
}

public interface IDomainEvent { }

public class DomainException(string message) : Exception(message);
```

Read it slowly — every line pays off later:

- **`Entity<TId>`** gives anything with an identity a typed `Id`. The setter is `protected`, so only the
  entity itself may assign it: nothing outside can quietly reassign an order's identity. The `TId` is generic
  because in [Phase 5](05-domain-model.md) we give each entity its *own* ID type (`SalesOrderId`,
  `CustomerId`) rather than sharing a bare `long`.
- **`where TId : struct`** says the ID must be a value type. Our ID types will be `readonly record struct`s.
  ⚠️ Note that the constraint is repeated on `AggregateRoot<TId>` — C# does **not** inherit a constraint from
  the base class, and leaving it off the derived class is a compiler error (`CS0453`). This trips people up
  the first time.
- **`AggregateRoot<TId>`** is an entity that also collects **domain events** — a running list of "things that
  happened" (`SalesOrderSubmitted`, `SalesOrderApproved`). `Raise` is `protected` so *only the aggregate*
  announces its own events; `DomainEvents` is exposed as `IReadOnlyList` so callers can read them but never
  add one from outside. [Phase 8](08-approval-cancellation.md) is where we dispatch them, after the
  transaction commits.
- **`IDomainEvent`** is an empty **marker interface** — it holds no members, it just lets the compiler (and
  our dispatcher) recognise "this record is a domain event."
- **`DomainException`** is what the domain throws when a *business rule* is broken ("this customer is
  blocked"). Giving it its own type is what later lets [Phase 11](11-cross-cutting.md) turn exactly these
  into an HTTP **422 Unprocessable Entity** — "I understood your request, but the business says no" — while
  malformed input becomes a **400** and a genuine bug still surfaces as a **500**.

> ❓ **"An `AggregateRoot`? Isn't that just an entity?"** An *aggregate root* is the one entity in a cluster
> that the outside world is allowed to touch. Nothing loads or edits a `SalesOrderLine` directly — you go
> through the `SalesOrder` that owns it. That's what makes it possible for the order to guarantee its own
> rules (like "lines can only change on a draft"), because there's no back door around it. If this feels
> abstract now, it becomes concrete in [Phase 5](05-domain-model.md); you don't need to fully grasp it yet.

Everything above compiles on its own — no other project, no packages. That's the point of putting it here.

## 5. NuGet packages (install what this phase needs)

Right now the only thing that has to run is the **host**. The `dotnet new web` template already gives us a
minimal API with a single endpoint. We'll replace that with a proper `GET /health` endpoint later in this
phase. For now, we add exactly **one** package: **Swashbuckle**, so we can *browse* the running app in a
Swagger UI instead of only curling it.

> ❓ **"Doesn't .NET ship OpenAPI support now?"** It does — since .NET 9, `Microsoft.AspNetCore.OpenApi`
> generates the OpenAPI *document* out of the box, and Swashbuckle is no longer in the default template. But
> a document is a JSON file, not a page you can click. We want the **Swagger UI** — the interactive page with
> an **Authorize** button, which we'll be leaning on hard from [Phase 4](04-jwt-auth.md) onward to paste a
> token and fire real requests. Swashbuckle still gives us both in one package, so that's what we take.

### 5b. One version per package everywhere: Central Package Management

This solution will grow to eight-plus projects, and the same package (say `Dapper`, once we add it) can get
referenced from several `.csproj` files. If each pins its *own* version, they drift — `Dapper 2.1.35` here,
`2.1.79` there — and you get subtle runtime mismatches and painful upgrades (edit ten files to bump one
version). **.NET's Central Package Management (CPM)** fixes this: you declare **each package version once**,
in a single `Directory.Packages.props` at the solution root, and each project references the package
**without a version**. We set it up now, at solution-setup time, and it stays **one line per package** — we
add a line the moment a phase introduces a new package, and never before.

> 💡 **Analogy: one shopping list for the whole household.** Instead of each person keeping their own
> (conflicting) list of "which brand of milk," there's *one* list on the fridge. Everybody buys from it, so
> the whole house is always on the same version. To change milk, you edit *one* line.

Create `Directory.Packages.props` next to `Erp.slnx`.

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <!-- one line per package used ANYWHERE in the solution (known-good for .NET 10) -->
    <PackageVersion Include="Swashbuckle.AspNetCore" Version="10.2.3" />
    <!-- Phase 2 adds Carter + FluentValidation here, Phase 3 adds Dapper, Phase 4 the JWT packages, … -->
  </ItemGroup>
</Project>
```

If you're adding CPM to an **existing project** that already has package references with versions, remove the
`Version="..."` attribute from those `<PackageReference>` entries—the version now belongs in
`Directory.Packages.props`.

With CPM on, a `.csproj` lists the package with **no** `Version` attribute—the version comes from the props:

```xml
<!-- src/Modules/Sales/Erp.Modules.Sales.csproj -->
<PackageReference Include="FluentValidation" />
<PackageReference Include="Dapper" />
```

Most projects should stop here. They automatically inherit the version defined in
`Directory.Packages.props`.

> 💡 **Need a different version for one project?** It's uncommon, but sometimes a project has to stay on an
> older or newer package version — during a gradual migration, say. Don't put `Version="..."` back on the
> `PackageReference`; that's the thing CPM forbids. Use `VersionOverride` instead. The central line still
> declares what everyone *else* gets:

```xml
<!-- Directory.Packages.props — the version the whole solution uses -->
<PackageVersion Include="Dapper" Version="2.1.79" />
```

```xml
<!-- src/LegacyTool/LegacyTool.csproj — this one project opts out -->
<ItemGroup>
  <PackageReference Include="Dapper" VersionOverride="2.1.35" />
</ItemGroup>
```

The rest of the solution uses **Dapper 2.1.79**, while `LegacyTool` alone uses **2.1.35**. We never need
this in our solution — it's here so you recognise it when you meet it in someone else's.

Now install the package for this phase:

```bash
dotnet add src/Erp.Api package Swashbuckle.AspNetCore
```

> 🔧 **How it interacts with `dotnet add package`.** Once CPM is enabled, `dotnet add ... package Dapper`
> adds a version-less `<PackageReference Include="Dapper" />` to the `.csproj` **and** a
> `<PackageVersion Include="Dapper" Version="…" />` to `Directory.Packages.props` (creating the entry if
> it's missing). So the `dotnet add package` commands in §5 still work — CPM just changes *where the version
> is written*. If you add a package and get *`NU1008: Projects that use central package version management
> should not define the version on the PackageReference`*, delete the stray `Version="…"` from that
> `.csproj` — it belongs in the props file. Pin new versions in one place, forever.

> 📌 **Version numbers drift over time.** The exact versions above are a *known-good set for .NET 10 as of
> writing*; when you run `dotnet restore` you may be offered newer ones. Bumping them is now a one-file edit —
> which is the entire point.

## 6. Configuration

**This phase needs no special configuration** — the `dotnet new web` template's default
`appsettings.json` is enough to run the application. We add configuration the same way we
add packages: **only when a phase first needs it.**

- The **database connection string** arrives in [Phase 3](03-database-schema.md), where we first talk to SQL Server.
- The **JWT settings** (issuer, signing key, token lifetimes) arrive in [Phase 4](04-jwt-auth.md), where we add login.

One thing to know now, so the later phases make sense: there's a **single database** (`Erp`) that will hold
every module's schema (`sales`, later `inventory`) — this is a *monolith*. The module walls live in the
*code* and *schemas*, not in separate databases. (That split only happens if you ever extract a module.)



## 7. Program.cs — the composition root

The template already gives you a `Program.cs` with a minimal API that responds to `GET /`. For this phase we
make a few small changes: enable Swagger, replace the default endpoint with `GET /health`, and add a
`public partial Program` for future integration tests. The module-wiring lines
(`AddSharedInfrastructure`, `AddSalesModule`, `MapCarter`) arrive in Phase 2 when there's actually a
mediator and an endpoint to wire:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();               // Swashbuckle — the browsable API page

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok("healthy"));

app.Run();

public partial class Program { }   // lets tests boot the app later (we use this in the testing phase)
```

> ❓ **"What's different from the template?"**
>
> - Added `AddEndpointsApiExplorer()` so Swagger can discover minimal API endpoints.
> - Added `AddSwaggerGen()` to generate the OpenAPI document.
> - Added `UseSwagger()` and `UseSwaggerUI()` in Development.
> - Replaced the template's `GET /` endpoint with `GET /health`.
> - Added `public partial class Program { }` for future integration testing.

> ❓ **"What's `public partial class Program { }`?"** It makes the auto-generated `Program` type *public* so
> that, later, the end-to-end tests can boot the whole app with `WebApplicationFactory<Program>`. Adding this
> one line now saves editing `Program.cs` again then—it's harmless until then.

> 🧭 **From WebForms:** `Program.cs` is your `Global.asax` + `web.config` wiring, in one readable place—a
> *table of contents* for the app, not a junk drawer. Right now the table has only a handful of entries;
> each phase adds another.

## 8. Run it — your first checkpoint ✅

```
Browser → GET /health → ASP.NET routing → () => Results.Ok("healthy") → 200 "healthy"
```

Run the API:

```bash
dotnet run --project src/Erp.Api
```

By default, `dotnet run` uses the **Debug** configuration.

If you want to run the optimized Release build instead:

```bash
dotnet run --project src/Erp.Api --configuration Release
```

> 💡 You'll use `dotnet run --project src/Erp.Api` throughout this tutorial. It always runs the API directly
> from the project, regardless of which project Visual Studio has marked as the startup project. That makes
> the commands consistent whether you're using Visual Studio, VS Code, Rider, or just the terminal.

The console prints the URL it's listening on (something like `http://localhost:5234`). Verify **both**:

- **Health check:** `http://localhost:5234/health`
- **Swagger UI:** `http://localhost:5234/swagger`

You can also test the health endpoint from a terminal:

```bash
curl http://localhost:5234/health
```

### Optional: Use a fixed local port

The random `5xxx` port can change between machines and even between projects. If you'd rather always use
the same address (for example, `http://localhost:8080`), edit
`src/Erp.Api/Properties/launchSettings.json`.

Find the profile you use (usually **http** or **https**) and change the
`applicationUrl`:

```json
{
  "profiles": {
    "http": {
      "commandName": "Project",
      "launchBrowser": true,
      "launchUrl": "swagger",
      "applicationUrl": "http://localhost:8080",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

If you also use HTTPS, you can specify both URLs:

```json
"applicationUrl": "http://localhost:8080;https://localhost:8081"
```

From then on, your API will consistently be available at:

- `http://localhost:8080/health`
- `http://localhost:8080/swagger`

If you see `healthy` and the Swagger page renders, **the frame stands** — the projects compile, the module
walls are in place, and the host runs. That's a complete, working Phase 1. Everything from here just adds
rooms to a house that already has power.

## 9. Common mistakes & best practices

- ❌ **A database reference in a module's `Domain/` folder.** ✅ Keep the Domain pure; it uses only
  `BuildingBlocks`. (The reference graph in §3 already makes the wrong thing impossible to compile.)
- ❌ **Making `Sales` reference `Inventory` (or vice versa).** ✅ Never — modules talk through a `Shared`
  contract. The missing reference *is* the wall.
- ❌ **Making a module reference `Shared.Infrastructure` "because it'll need the database eventually."**
  ✅ It needs the *interface* (`IDbConnectionFactory`, in `Shared.BuildingBlocks`), never the implementation. Only
  the host references `Shared.Infrastructure`.
- ❌ **Generic `Commands/`/`Handlers/`/`Endpoints/` folders.** ✅ We group by *feature* (two files each) —
  you'll see the first feature folder in Phase 6; keep this shape from the start.
- ❌ **Installing every package "to be safe" up front.** ✅ Add each one in the phase that uses it, and let
  CPM keep the versions in one file.

---

## ✅ Check Yourself

> Try each one before you look. [Model answers](ANSWER-KEY.md#phase-1-solution-setup) — but attempting first is what makes them stick.

1. Why is the solution split by module + Shared kernel instead of four Clean Architecture layers?
2. What compiler-level thing stops `Sales` from reaching into `Inventory`?
3. `IDbConnectionFactory` lives in `Shared.BuildingBlocks`, but `SqlConnectionFactory` lives in
   `Shared.Infrastructure`. Why are they in different projects, and which one is the `Sales` module allowed
   to see?
4. Why does this phase install only Swashbuckle and no other packages?
5. What does Central Package Management give you, and where do package versions live once it's on?

---

← Previous: [Overview — Start Here](00-START-HERE.md) · Next: [Phase 2 — Your own mediator vs. the free one](02-mediator-own-vs-free.md) →
