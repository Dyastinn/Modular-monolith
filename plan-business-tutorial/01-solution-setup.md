# Phase 1 — Solution Setup: The Skeleton for Our Modular Monolith

> 📎 Slow version of these ideas: [beginner Phase 1](../plan-beginner-friendly/02-phase-1-skeleton.md).
> This phase reuses that exact project structure, named for our ERP.

---

## 1. What we're building

An empty-but-running **modular monolith** solution: a `Shared` kernel, a `Sales` module, the NuGet packages
we'll need across the tutorial, configuration wired up, and a health-check endpoint that proves the app
starts. **No business logic yet** — we're framing the house before decorating rooms.

By the end: `GET /health` returns `"healthy"`, and the projects compile with the module walls in place.

> 🧭 **From WebForms:** in a WebForms app, "the solution" was usually one Web Application project with a pile
> of `.aspx` pages and a `BLL`/`DAL` folder. Here the top-level split is by **business capability** (a
> `Sales` module), and inside a module by **feature**. There is no `BLL`/`DAL` layer, and there is no
> Clean-Architecture `Domain`/`Application`/`Infrastructure` split either — those scatter one feature across
> many projects. We organize by *what the code is about*, not *what technical kind it is*.

## 2. Why it's designed this way

We use a **modular monolith with Vertical Slice Architecture**
([beginner Phase 0 §3](../plan-beginner-friendly/01-phase-0-orientation.md)). Two rules replace the old
four-layer diagram:

1. **Modules are islands** — `Sales` and (later) `Inventory` are separate projects that never reference each
   other's internals. They talk through a published contract in `Shared`.
2. **Keep the Domain pure** — inside a module, `Features/` and `Infrastructure/` use `Domain/`; `Domain/`
   uses only `Shared.BuildingBlocks`. Same testability as Clean Architecture's inner circle, without the
   ceremony.

## 3. The projects and folder structure

Create the solution exactly like
[beginner Phase 1 §1](../plan-beginner-friendly/02-phase-1-skeleton.md) (the `dotnet new` commands are
there). Here's the shape, filled in with the folders this tutorial populates:

```
Erp.slnx
├── src/
│   ├── Shared/
│   │   ├── Erp.Shared.BuildingBlocks/   # Entity, AggregateRoot, ValueObject, IDomainEvent, DomainException
│   │   ├── Erp.Shared.Common/           # Result/Error, IClock, PagedResult, IStockReservation contract
│   │   ├── Erp.Shared.Messaging/        # IRequest, ICommand, IQuery, IRequestHandler, ISender, behaviors
│   │   └── Erp.Shared.Infrastructure/   # Dapper connection factory, JWT/auth, ICurrentUser, Carter wiring,
│   │                                    #   global error handling, AddSharedInfrastructure()
│   │
│   ├── Modules/
│   │   └── Sales/  (Erp.Modules.Sales)
│   │       ├── Domain/                  # SalesOrder aggregate, Customer, Product, value objects, events
│   │       ├── Features/                # one folder per feature (two files each)
│   │       │   ├── CreateSalesOrder/    #   CreateSalesOrderHandler.cs + CreateSalesOrderEndpoint.cs (Phase 6)
│   │       │   ├── SubmitSalesOrder/    #   (Phase 7)
│   │       │   ├── ApproveSalesOrder/   #   (Phase 8)
│   │       │   ├── CancelSalesOrder/    #   (Phase 8)
│   │       │   ├── UpdateSalesOrder/    #   (Phase 9)
│   │       │   ├── GetSalesOrder/       #   (Phase 10)
│   │       │   └── SearchSalesOrders/   #   (Phase 10)
│   │       ├── Infrastructure/          # Dapper repositories, TypeHandlers, Sql/ scripts
│   │       └── DependencyInjection.cs   # AddSalesModule(): registers this module's handlers/validators/repos
│   │
│   └── Erp.Api/
│       ├── Program.cs                   # composition root: AddSharedInfrastructure + AddSalesModule + MapCarter
│       ├── Auth/                        # the Login slice (see §4) + JWT setup helpers
│       ├── appsettings.json
│       └── appsettings.Development.json
│
└── tests/
    ├── Erp.Modules.Sales.UnitTests/
    └── Erp.Integration.Tests/
```

Each module keeps its SQL scripts in its own `Infrastructure/Sql/` folder (the `sales` schema; `inventory`
arrives in Phase 7). There is no central `db/` folder — data ownership follows the module.

**Dependency references** (`dotnet add reference`):

```
Erp.Api                   → Erp.Modules.Sales, Erp.Modules.Inventory, Erp.Shared.Infrastructure
Erp.Modules.Sales         → Erp.Shared.{BuildingBlocks, Common, Messaging, Infrastructure}
Erp.Modules.Inventory     → Erp.Shared.{Common, Infrastructure}          (Phase 7)
Erp.Shared.Infrastructure → Erp.Shared.Messaging, Erp.Shared.Common, Erp.Shared.BuildingBlocks
Erp.Shared.Messaging      → Erp.Shared.Common, Erp.Shared.BuildingBlocks
Erp.Shared.BuildingBlocks → (nothing)
Erp.Shared.Common         → (nothing)
```

No module references another module — the compiler enforces the island rule.

> ❓ **"Why does `Erp.Shared.Messaging` reference `BuildingBlocks`?"** The mediator contracts alone don't need
> it — but the **domain-event dispatch** contracts we add in [Phase 8](08-approval-cancellation.md)
> (`IDomainEventHandler<TEvent>`, `IDomainEventDispatcher`) mention `IDomainEvent` and `AggregateRoot<TId>`,
> which live in `BuildingBlocks`. So Messaging references BuildingBlocks from the start. It's a safe edge —
> `BuildingBlocks` depends on nothing, so no module wall is crossed.

## 4. Why each folder exists (no mystery folders)

| Folder | Why it exists | Why *here* |
|---|---|---|
| `Shared.BuildingBlocks` | `Entity`, `AggregateRoot`, `IDomainEvent`, `DomainException`. | Generic scaffolding every module's domain reuses; depends on nothing. |
| `Shared.Common` | `Result`/`Error`, `IClock`, `PagedResult`, and cross-module **contracts** like `IStockReservation`. | Pure, shared types both modules may depend on without depending on each other. |
| `Shared.Messaging` | The hand-rolled mediator contracts + `Sender` + behaviors. | Every module sends/handles through one mediator. |
| `Shared.Infrastructure` | Dapper connection factory, JWT token service, `ICurrentUser`, Carter/auth/error wiring. | Framework/crypto/db plumbing; a module's Domain never sees it. |
| `Sales/Domain` | The `SalesOrder` that knows how to submit/approve itself; `Customer`, `Product`. | Pure business objects; depend only on `BuildingBlocks`. |
| `Sales/Features/<Feature>` | One folder per feature, **two files** (handler + endpoint). | Everything for "create a sales order" together, not scattered by technical kind. |
| `Sales/Infrastructure` | Dapper repositories, ID `TypeHandler`s, `Sql/` scripts. | This module's data access; owned, not shared. |
| `Sales/DependencyInjection.cs` | `AddSalesModule()` — the module's single registration seam. | The one public way the host plugs the module in. |
| `Api/Auth` | The `Login` slice + JWT setup. | Authentication is host/platform concern; the business modules stay auth-agnostic and just declare `RequireAuthorization`. |

> ❓ **"Why is `Login` in the Api/Shared and not a module?"** Authentication is *cross-cutting platform*, not
> a business capability of Sales. The JWT token service and `ICurrentUser` live in `Shared.Infrastructure`;
> the `Login` endpoint sits with the host. In a larger ERP, user management would become its own `Identity`
> module — a natural extraction we note in [Phase 13](13-wrap-up-checklist.md). For this tutorial, keeping
> Sales purely about sales is the clearer boundary.

## 5. NuGet packages (install these now)

**`Erp.Shared.Messaging`** (the mediator + behaviors need DI, logging, and validation abstractions):
```bash
dotnet add src/Shared/Erp.Shared.Messaging package Microsoft.Extensions.DependencyInjection.Abstractions
dotnet add src/Shared/Erp.Shared.Messaging package Microsoft.Extensions.Logging.Abstractions
dotnet add src/Shared/Erp.Shared.Messaging package FluentValidation
```

**`Erp.Shared.Infrastructure`:**
```bash
dotnet add src/Shared/Erp.Shared.Infrastructure package Dapper
dotnet add src/Shared/Erp.Shared.Infrastructure package Microsoft.Data.SqlClient
dotnet add src/Shared/Erp.Shared.Infrastructure package Carter
dotnet add src/Shared/Erp.Shared.Infrastructure package System.IdentityModel.Tokens.Jwt
dotnet add src/Shared/Erp.Shared.Infrastructure package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add src/Shared/Erp.Shared.Infrastructure package Microsoft.Extensions.Configuration.Abstractions
dotnet add src/Shared/Erp.Shared.Infrastructure package Microsoft.Extensions.DependencyInjection.Abstractions
```
(This project references the ASP.NET Core shared framework — add `<FrameworkReference Include="Microsoft.AspNetCore.App" />` to its `.csproj` — so it can host Carter and auth wiring.)

**`Erp.Modules.Sales`:**
```bash
dotnet add src/Modules/Sales package FluentValidation
dotnet add src/Modules/Sales package FluentValidation.DependencyInjectionExtensions   # AddValidatorsFromAssembly (Phase 2)
dotnet add src/Modules/Sales package Dapper
dotnet add src/Modules/Sales package Microsoft.Extensions.DependencyInjection.Abstractions
```

**`Erp.Modules.Inventory`** (created in [Phase 7](07-submit-reserve-concurrency.md)):
```bash
dotnet add src/Modules/Inventory package Dapper
dotnet add src/Modules/Inventory package Microsoft.Extensions.DependencyInjection.Abstractions
```

**`Erp.Api`:**
```bash
dotnet add src/Erp.Api package Carter
dotnet add src/Erp.Api package Swashbuckle.AspNetCore
# If you ever swap the hand-rolled mediator for the free one (Phase 2 keeps hand-rolled):
# dotnet add src/Modules/Sales package Mediator.Abstractions
```

> ℹ️ **A note on Swagger in .NET 10.** Since .NET 9, the built-in template ships Microsoft's own
> **`Microsoft.AspNetCore.OpenApi`** package (`builder.Services.AddOpenApi()` / `app.MapOpenApi()`) as the
> default way to produce an OpenAPI document, and **Swashbuckle is now a third-party package** rather than
> the built-in default. We use Swashbuckle here purely because its `SwaggerUI` gives you a clickable
> in-browser "try it out" page out of the box, which is friendlier while learning. Either is fine; if you
> want to stay on Microsoft's built-in path, use `AddOpenApi()` and pair it with a UI of your choice. Not
> a correctness issue — just so ".NET 10" and "Swashbuckle" together don't look out of date to you.

**Test projects:** `dotnet add tests/Erp.Modules.Sales.UnitTests package FluentAssertions`.

> ❓ **"JWT and Carter are new packages — didn't we say no new tech?"** JWT was in the required stack for
> *this* tutorial, and `JwtBearer` is the built-in ASP.NET way — not a framework like MassTransit. Carter is
> a thin layer over the minimal APIs you'd use anyway; it just lets each feature own its endpoint. Both are
> sanctioned "batteries," not architecture we're smuggling in.

### 5b. One version per package everywhere: Central Package Management

With eight-plus projects, the same package (say `Dapper`) gets referenced from several `.csproj` files. If
each pins its *own* version, they drift — `Dapper 2.1.35` here, `2.1.66` there — and you get subtle runtime
mismatches and painful upgrades (edit ten files to bump one version). **.NET's Central Package Management
(CPM)** fixes this: you declare **every package version once**, in a single `Directory.Packages.props` at the
solution root, and each project references the package **without a version**.

> 💡 **Analogy: one shopping list for the whole household.** Instead of each person keeping their own
> (conflicting) list of "which brand of milk," there's *one* list on the fridge. Everybody buys from it, so
> the whole house is always on the same version. To change milk, you edit *one* line.

Create `Directory.Packages.props` next to `Erp.slnx` (the repo already ships one — open it and read along):

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <!-- one line per package used ANYWHERE in the solution (known-good for .NET 10) -->
    <PackageVersion Include="Carter" Version="10.0.0" />
    <PackageVersion Include="Dapper" Version="2.1.79" />
    <PackageVersion Include="FluentValidation" Version="12.1.1" />
    <PackageVersion Include="FluentValidation.DependencyInjectionExtensions" Version="12.1.1" />
    <PackageVersion Include="Microsoft.Data.SqlClient" Version="7.0.2" />
    <PackageVersion Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.9" />
    <PackageVersion Include="System.IdentityModel.Tokens.Jwt" Version="8.19.1" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.Abstractions" Version="10.0.9" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.9" />
    <PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.9" />
    <PackageVersion Include="Swashbuckle.AspNetCore" Version="10.2.3" />
    <PackageVersion Include="FluentAssertions" Version="8.10.0" />
    <!-- plus the test-SDK packages (xunit, Microsoft.NET.Test.Sdk, coverlet) — see the repo's file -->
  </ItemGroup>
</Project>
```

With CPM on, a `.csproj` lists the package with **no** `Version` attribute — the version comes from the props:

```xml
<!-- src/Modules/Sales/Erp.Modules.Sales.csproj -->
<PackageReference Include="FluentValidation" />          <!-- version resolved from Directory.Packages.props -->
<PackageReference Include="Dapper" />
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

## 6. Configuration: connection string and JWT settings

`appsettings.json` (no secrets):

```json
{
  "ConnectionStrings": { "ErpDb": "" },
  "Jwt": { "Issuer": "Erp.Api", "Audience": "Erp.Client", "SigningKey": "", "AccessTokenMinutes": 60 },
  "Logging": { "LogLevel": { "Default": "Information" } }
}
```

`appsettings.Development.json` (local dev only):

```json
{
  "ConnectionStrings": {
    "ErpDb": "Server=(localdb)\\MSSQLLocalDB;Database=Erp;Trusted_Connection=True;TrustServerCertificate=True"
  },
  "Jwt": { "SigningKey": "dev-only-super-secret-key-change-me-32chars-min" }
}
```

One database (`Erp`) holds both modules' schemas (`sales`, `inventory`) — a monolith. The module walls are
in the *code* and *schemas*, not separate databases (that split comes only if you extract a module).

> 🔐 **Best practice:** the `SigningKey` is a secret. Real life: **user-secrets** in dev
> (`dotnet user-secrets set "Jwt:SigningKey" "..."`), env vars / a vault in prod. Committing a secret is a
> real incident. Min 32 chars for the signing algorithm.

## 7. Program.cs — the composition root

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSharedInfrastructure(builder.Configuration);  // mediator, Dapper, JWT, Carter, ICurrentUser
builder.Services.AddSalesModule(builder.Configuration);           // the Sales module plugs itself in
// builder.Services.AddInventoryModule(builder.Configuration);    // (Phase 7)

var app = builder.Build();

if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
// app.UseAuthentication(); app.UseAuthorization();   // (Phase 4)

app.MapGet("/health", () => Results.Ok("healthy"));
app.MapCarter();   // discovers every feature's ICarterModule endpoint

app.Run();

public partial class Program { }   // makes Program visible to the integration tests (Phase 12)
```

> ❓ **"What's `public partial class Program { }`?"** It makes the auto-generated `Program` public so the
> end-to-end tests in [Phase 12](12-testing.md) can boot the app with `WebApplicationFactory<Program>`. Add
> it now.

> 🧭 **From WebForms:** `Program.cs` is your `Global.asax` + `web.config` wiring, in one readable place —
> a *table of contents* for the app, not a junk drawer. Each `Add*Module` line is a department checking in.

## 8. How a request flows (even now)

```
Browser → GET /health → ASP.NET routing → () => Results.Ok("healthy") → 200 "healthy"
```

```bash
dotnet run --project src/Erp.Api
curl http://localhost:5xxx/health      # → healthy
```

If you see `healthy`, the frame stands.

## 9. Common mistakes & best practices

- ❌ **A database reference in a module's `Domain/` folder.** ✅ Keep the Domain pure; it uses only
  `BuildingBlocks`.
- ❌ **Making `Sales` reference `Inventory` (or vice versa).** ✅ Never — they talk through a `Shared`
  contract. The missing reference is the wall.
- ❌ **Generic `Commands/`/`Handlers/`/`Endpoints/` folders.** ✅ Group by *feature*, two files each.
- ❌ **Routes piled into `Program.cs`.** ✅ Each feature owns its Carter endpoint; `Program.cs` calls
  `MapCarter()`.
- ❌ **Committing real secrets.** ✅ Dev key in the Development file only; real keys in user-secrets / env.
- ✅ **One `Add*Module` / `AddSharedInfrastructure` per project.** `Program.cs` reads like a table of
  contents.

---

## ✅ Check Yourself

1. Why is the solution split by module + Shared kernel instead of four Clean Architecture layers?
2. What compiler-level thing stops `Sales` from reaching into `Inventory`?
3. What does Carter change about where endpoints live?
4. Where does the JWT signing key belong, and where must it never be?
5. Why is `Login` a host/shared concern rather than a Sales feature?

> 👉 Next: [Phase 2 — Your own mediator vs. the free one on GitHub](02-mediator-own-vs-free.md).
