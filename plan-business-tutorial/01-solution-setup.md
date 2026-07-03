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
Erp.Api                   → Erp.Modules.Sales, Erp.Shared.Infrastructure
Erp.Modules.Sales         → Erp.Shared.{BuildingBlocks, Common, Messaging, Infrastructure}
Erp.Shared.Infrastructure → Erp.Shared.Messaging, Erp.Shared.Common
Erp.Shared.Messaging      → Erp.Shared.Common
Erp.Shared.BuildingBlocks → (nothing)
Erp.Shared.Common         → (nothing)
```

No module references another module — the compiler enforces the island rule.

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

**`Erp.Shared.Infrastructure`:**
```bash
dotnet add src/Shared/Erp.Shared.Infrastructure package Dapper
dotnet add src/Shared/Erp.Shared.Infrastructure package Microsoft.Data.SqlClient
dotnet add src/Shared/Erp.Shared.Infrastructure package Carter
dotnet add src/Shared/Erp.Shared.Infrastructure package System.IdentityModel.Tokens.Jwt
dotnet add src/Shared/Erp.Shared.Infrastructure package Microsoft.AspNetCore.Authentication.JwtBearer
```
(This project references the ASP.NET Core shared framework — add `<FrameworkReference Include="Microsoft.AspNetCore.App" />` to its `.csproj` — so it can host Carter and auth wiring.)

**`Erp.Modules.Sales`:**
```bash
dotnet add src/Modules/Sales package FluentValidation
```

**`Erp.Api`:**
```bash
dotnet add src/Erp.Api package Carter
dotnet add src/Erp.Api package Swashbuckle.AspNetCore
# If you ever swap the hand-rolled mediator for the free one (Phase 2 keeps hand-rolled):
# dotnet add src/Modules/Sales package Mediator.Abstractions
```

**Test projects:** `dotnet add tests/Erp.Modules.Sales.UnitTests package FluentAssertions`.

> ❓ **"JWT and Carter are new packages — didn't we say no new tech?"** JWT was in the required stack for
> *this* tutorial, and `JwtBearer` is the built-in ASP.NET way — not a framework like MassTransit. Carter is
> a thin layer over the minimal APIs you'd use anyway; it just lets each feature own its endpoint. Both are
> sanctioned "batteries," not architecture we're smuggling in.

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
