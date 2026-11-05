# Lesson 14 — Testing: Proving It All Works, Automatically

Good day, developer! We've had unit tests since [lesson 03](03-domain-model.md), and they prove the business
rules. But some bugs can't live in a unit test: two requests racing for the last units of stock, a transaction
that doesn't really span two modules, a validator that silently stopped running. Today we'll catch those, by
running the whole API against a real PostgreSQL database. And we'll write tests for the architecture itself,
so nobody can quietly knock down a module wall. About two hours. Are you ready? Let's begin!

## What we'll build

| Test project | What it proves | Speed |
|---|---|---|
| `Erp.Modules.Sales.UnitTests` (from lesson 03) | Every business rule | Milliseconds, no database |
| `Erp.Integration.Tests` | Real requests through the real API and a real database: auth, workflow, races, outbox | Seconds |
| `Erp.Architecture.Tests` | The module rules: who may reference whom, what may be public | Milliseconds |

## Why we build it this way

### Most tests should be unit tests

The rules live in the domain, so most tests should test the domain, with no database: they're fast, they never
flake, and a failure points at exactly one rule. That's why lesson 03 came first.

### But some things only a database can prove

- Two requests reserving the last five kits *at the same instant*: only one may win. That depends on how
  PostgreSQL handles one `UPDATE`, not on any C#.
- Inventory really joining Sales' transaction: if it didn't, a rollback would leave stock reserved.
- Two people editing one draft: the second save must fail.
- `includeInternalTypes: true`: forget it, and validators silently stop running. Only a real request shows it.

For those, we start the **whole API in memory**, against a **real PostgreSQL database** made just for the
tests, and send it real HTTP requests.

### And the architecture can be tested too

The module walls are rules: a module references other modules only through their contracts; a module has one
public type; internals are shared only with the host and tests. A reviewer can miss a stray project reference
in a big change. A test can't.

## 1. Let the tests start the app

`WebApplicationFactory<Program>` starts our app inside the test process. It needs to see the `Program` class,
which top-level statements make internal. One line at the very end of `Program.cs` fixes that:

```csharp
public partial class Program { }   // lets the integration tests start the app
```

## 2. The integration test project

```bash
dotnet new classlib -n Erp.Integration.Tests -o tests/Erp.Integration.Tests
rm tests/Erp.Integration.Tests/Class1.cs
dotnet sln add tests/Erp.Integration.Tests
dotnet add tests/Erp.Integration.Tests reference src/Erp.Api

dotnet add tests/Erp.Integration.Tests package Microsoft.NET.Test.Sdk --version 17.14.1
dotnet add tests/Erp.Integration.Tests package xunit --version 2.9.3
dotnet add tests/Erp.Integration.Tests package xunit.runner.visualstudio --version 3.1.4
dotnet add tests/Erp.Integration.Tests package AwesomeAssertions --version 9.6.0
dotnet add tests/Erp.Integration.Tests package Microsoft.AspNetCore.Mvc.Testing --version 10.0.14
dotnet add tests/Erp.Integration.Tests package Npgsql --version 10.0.3
dotnet add tests/Erp.Integration.Tests package Dapper --version 2.1.79
```

`Microsoft.AspNetCore.Mvc.Testing` (MIT) provides `WebApplicationFactory`. Npgsql and Dapper let a test look
inside the database directly, to check what really got saved. As in lesson 03, delete the `<PropertyGroup>`,
then add `<IsPackable>false</IsPackable>` and `<Using Include="Xunit" />`.

Two tests need to reach inside modules: one edits a draft through `SalesDbContext`, one creates a throwaway
user through `UserManager<AppUser>`. Grant the test project access in `Erp.Modules.Sales.csproj` and
`Erp.Modules.Identity.csproj`:

```xml
<InternalsVisibleTo Include="Erp.Integration.Tests" />
```

## 3. The test host

```csharp
// tests/Erp.Integration.Tests/CustomWebAppFactory.cs
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Erp.Integration.Tests;

// The whole API in memory against its own database, dropped first so every run starts from migrations + seed.
public sealed class CustomWebAppFactory : WebApplicationFactory<Program>
{
    public static readonly string TestConnectionString =
        Environment.GetEnvironmentVariable("ERP_TEST_DB")
        ?? "Host=localhost;Port=5432;Database=erp_test;Username=postgres;Password=erp_dev_only";

    // Set to run the suite as the least-privilege erp_app (ERP_TEST_DB) against a database prepared by
    // deploy/postgres/create-roles.sql, with migrations run as erp_owner. That database is not dropped.
    private static readonly string? OwnerConnectionString = Environment.GetEnvironmentVariable("ERP_TEST_DB_OWNER");

    public const string TestApiKey = "test-suite-key";
    public const string Password = "Password123!";

    public CustomWebAppFactory()
    {
        if (OwnerConnectionString is null)
            DropTestDatabase();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:ErpDb", TestConnectionString);
        if (OwnerConnectionString is not null)
            builder.UseSetting("ConnectionStrings:ErpDbMigrations", OwnerConnectionString);
        builder.UseSetting("ApiKeys:tests", TestApiKey);
        builder.UseSetting("RateLimiting:RequestsPerMinute", "100000");
        builder.UseSetting("RateLimiting:AuthRequestsPerMinute", "100000");
        builder.UseSetting("Outbox:PollIntervalMs", "200");
    }

    protected override void ConfigureClient(HttpClient client)
    {
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiKey);
        base.ConfigureClient(client);
    }

    public sealed record Tokens(string AccessToken, string RefreshToken);

    public async Task<Tokens> LoginAsync(HttpClient client, string email, string password = Password)
    {
        var response = await client.PostAsJsonAsync("/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    public async Task<HttpClient> ClientFor(string email)
    {
        var client = CreateClient();
        var tokens = await LoginAsync(client, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    private static void DropTestDatabase()
    {
        var target = new NpgsqlConnectionStringBuilder(TestConnectionString);
        var admin = new NpgsqlConnectionStringBuilder(TestConnectionString) { Database = "postgres", Pooling = false };
        using var conn = new NpgsqlConnection(admin.ConnectionString);
        conn.Open();
        using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{target.Database}\" WITH (FORCE)", conn);
        drop.ExecuteNonQuery();
    }
}
```

Here's what each choice buys:

| Choice | Why |
|---|---|
| Its own database, `erp_test` | Tests never touch your development data |
| Dropped at the start of every run | Every run starts from the migrations and the seed, so yesterday's leftovers can't change today's results |
| `Development` environment | Migrations and dev seed run on startup, just like on your machine |
| Its own API key | Tests are a client app like any other, so they go through the real API-key check |
| Huge rate limits | The suite signs in dozens of times a minute |
| A fast outbox (200 ms) | The outbox test doesn't wait two seconds per check |
| `ERP_TEST_DB` / `ERP_TEST_DB_OWNER` | Point the whole suite at the least-privilege accounts from lesson 13, to prove they're enough |

All the tests share one database, so they must not run at the same time. A **collection** makes xUnit start
the app once and run the classes one after another:

```csharp
// tests/Erp.Integration.Tests/IntegrationCollection.cs
namespace Erp.Integration.Tests;

// One shared test database: one collection boots the app once and runs the classes in series.
[CollectionDefinition("Integration")]
public sealed class IntegrationCollection : ICollectionFixture<CustomWebAppFactory> { }
```

## 4. The race nobody can test by hand

```csharp
// tests/Erp.Integration.Tests/InventoryReservationTests.cs
using AwesomeAssertions;
using Dapper;
using Erp.Modules.Inventory.Contracts;
using Erp.Shared.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Erp.Integration.Tests;

// Each caller gets its own DI scope (connection, transaction), like two simultaneous requests.
[Collection("Integration")]
public class InventoryReservationTests(CustomWebAppFactory factory)
{
    private const long RestrictedKitId = 3;   // dev seed: on_hand = 8, reserved = 0

    [Fact]
    public async Task Two_concurrent_reservations_for_the_last_units_only_one_wins()
    {
        await SetReserved(RestrictedKitId, 0);

        bool[] results = await Task.WhenAll(
            ReserveInOwnCommittedTransaction(RestrictedKitId, 5),
            ReserveInOwnCommittedTransaction(RestrictedKitId, 5));

        results.Count(r => r).Should().Be(1);
        (await GetReserved(RestrictedKitId)).Should().Be(5);   // the winner's 5; 3 still available

        await SetReserved(RestrictedKitId, 0);
    }

    [Fact]
    public async Task A_reservation_rolls_back_with_the_callers_transaction()
    {
        await SetReserved(RestrictedKitId, 0);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var conn = scope.ServiceProvider.GetRequiredService<ISharedConnection>().Connection;
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var stock = scope.ServiceProvider.GetRequiredService<IStockReservation>();

            (await stock.TryReserveAsync(RestrictedKitId, 5, tx, default)).Should().BeTrue();
            // disposed without a commit
        }

        (await GetReserved(RestrictedKitId)).Should().Be(0);   // Inventory really joined the caller's transaction
    }

    private async Task<bool> ReserveInOwnCommittedTransaction(long itemId, int qty)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var conn = scope.ServiceProvider.GetRequiredService<ISharedConnection>().Connection;
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var stock = scope.ServiceProvider.GetRequiredService<IStockReservation>();   // the public contract only

        var reserved = await stock.TryReserveAsync(itemId, qty, tx, default);
        await tx.CommitAsync();   // commit, so the race is real
        return reserved;
    }

    private static async Task<int> GetReserved(long itemId)
    {
        await using var conn = new NpgsqlConnection(CustomWebAppFactory.TestConnectionString);
        return await conn.ExecuteScalarAsync<int>(
            "SELECT reserved FROM inventory.stock_item WHERE item_id = @itemId", new { itemId });
    }

    private static async Task SetReserved(long itemId, int reserved)
    {
        await using var conn = new NpgsqlConnection(CustomWebAppFactory.TestConnectionString);
        await conn.ExecuteAsync(
            "UPDATE inventory.stock_item SET reserved = @reserved WHERE item_id = @itemId", new { itemId, reserved });
    }
}
```

`Task.WhenAll` starts both reservations at once, each in its own scope, so each has its own connection and
transaction, exactly like two real requests. If the reservation checked in C# before writing, both would win
now and then. Here, exactly one wins, every time. And the second test proves the transaction really spans the
modules: dispose it without committing, and Inventory's change disappears too.

## 5. The lost update in the lines

```csharp
// tests/Erp.Integration.Tests/ConcurrencyTests.cs
using AwesomeAssertions;
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Integration.Tests;

[Collection("Integration")]
public class ConcurrencyTests(CustomWebAppFactory factory)
{
    // Two people load the same draft and both replace its lines: the second save must fail, not overwrite.
    [Fact]
    public async Task Second_of_two_concurrent_line_edits_is_rejected()
    {
        var orderId = await CreateDraft();

        await using var alice = factory.Services.CreateAsyncScope();
        await using var bob = factory.Services.CreateAsyncScope();
        var aliceDb = alice.ServiceProvider.GetRequiredService<SalesDbContext>();
        var bobDb = bob.ServiceProvider.GetRequiredService<SalesDbContext>();
        var aliceOrder = await aliceDb.SalesOrders.SingleAsync(o => o.Id == orderId);
        var bobOrder = await bobDb.SalesOrders.SingleAsync(o => o.Id == orderId);

        aliceOrder.ClearLines();
        aliceOrder.AddLine(new ItemId(1), new Quantity(3), new Money(25));
        bobOrder.ClearLines();
        bobOrder.AddLine(new ItemId(2), new Quantity(1), new Money(40));

        await aliceDb.SaveChangesAsync();
        var bobSaves = () => bobDb.SaveChangesAsync();
        await bobSaves.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    private async Task<SalesOrderId> CreateDraft()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        var now = DateTime.UtcNow;
        var order = SalesOrder.Create(await db.NextOrderNumberAsync(now, default), new CustomerId(1), new UserId(1), now);
        order.AddLine(new ItemId(1), new Quantity(1), new Money(25));
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }
}
```

Delete the `SaveChangesAsync` override from [lesson 09](09-update-order.md) and run this test: it fails,
because Bob's save goes through. Put the override back: it passes. That's how you know a test really guards
the thing it's named after.

## 6. Requests through the front door

The rest of the integration tests send real HTTP. Here's the shape of one, from the workflow tests:

```csharp
[Fact]
public async Task High_value_order_waits_for_a_manager_who_did_not_create_it()
{
    var rep = await factory.ClientFor(Rep);
    var manager = await factory.ClientFor(Manager);
    var id = await CreateOrder(rep, customerId: 5, itemId: 4, quantity: 1);
    await rep.PostAsync($"/sales-orders/{id}/submit", null);
    (await StatusOf(rep, id)).Should().Be("PendingApproval");

    (await manager.PostAsync($"/sales-orders/{id}/approve", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

    (await StatusOf(rep, id)).Should().Be("Approved");
}
```

`ClientFor(...)` signs in through the real `/auth/login` and returns an `HttpClient` carrying the token. The
full set lives in the reference build; write yours in the same style:

| File | Its tests prove |
|---|---|
| [SalesOrderWorkflowTests.cs](../course-validation/business/tests/Erp.Integration.Tests/SalesOrderWorkflowTests.cs) | Auto-approval; manager approval; no self-approval; reps can't approve (403); reject releases stock; pending orders count against credit; blocked and over-limit customers (422); bare search works; a page past the end keeps the real total; **an order with no lines is rejected (the guard for `includeInternalTypes`)** |
| [AuthTests.cs](../course-validation/business/tests/Erp.Integration.Tests/AuthTests.cs) | Wrong password is 401; `/auth/me` returns the role; refresh works; `/auth/register` is 404; **five wrong passwords lock an account**; sign-out-everywhere kills refresh tokens |
| [PlatformTests.cs](../course-validation/business/tests/Erp.Integration.Tests/PlatformTests.cs) | Health endpoints; security headers and `X-Trace-Id`; **an approval event is delivered through the outbox** |

Two of those deserve a closer look. The lockout test creates a throwaway user through `UserManager`, so
locking it doesn't lock a dev user the other tests need. And the outbox test polls the `outbox_message` table
until the event's `processed_utc` is set, giving up after ten seconds, because delivery happens on a
background timer, not inside the request.

## 7. Architecture tests

```bash
dotnet new classlib -n Erp.Architecture.Tests -o tests/Erp.Architecture.Tests
rm tests/Erp.Architecture.Tests/Class1.cs
dotnet sln add tests/Erp.Architecture.Tests
dotnet add tests/Erp.Architecture.Tests reference src/Erp.Api
dotnet add tests/Erp.Architecture.Tests package Microsoft.NET.Test.Sdk --version 17.14.1
dotnet add tests/Erp.Architecture.Tests package xunit --version 2.9.3
dotnet add tests/Erp.Architecture.Tests package xunit.runner.visualstudio --version 3.1.4
dotnet add tests/Erp.Architecture.Tests package AwesomeAssertions --version 9.6.0
```

Referencing the host pulls in every module, so the tests can inspect all of them. No extra library needed:
.NET's own reflection can read an assembly's references, its public types and its `InternalsVisibleTo` grants.

```csharp
// tests/Erp.Architecture.Tests/ModuleBoundaryTests.cs
using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Erp.Modules.Identity;
using Erp.Modules.Inventory;
using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Sales;

namespace Erp.Architecture.Tests;

// CLAUDE.md §4 module rules as tests: a reviewer can miss a stray reference or `public` class; these can't.
public class ModuleBoundaryTests
{
    private static readonly Assembly[] Modules =
        [typeof(IdentityModule).Assembly, typeof(SalesModule).Assembly, typeof(InventoryModule).Assembly];

    private static readonly Assembly[] ContractsAssemblies = [typeof(IStockReservation).Assembly];

    [Fact]
    public void A_module_references_other_modules_only_through_their_Contracts()
    {
        foreach (var module in Modules)
        {
            var illegal = module.GetReferencedAssemblies()
                .Select(a => a.Name!)
                .Where(n => n.StartsWith("Erp.Modules.") && !n.EndsWith(".Contracts"));

            illegal.Should().BeEmpty($"{module.GetName().Name} may reference another module's .Contracts only");
        }
    }

    [Fact]
    public void Contracts_reference_no_module_so_no_cycle_can_form()
    {
        foreach (var contracts in ContractsAssemblies)
            contracts.GetReferencedAssemblies().Select(a => a.Name!)
                .Where(n => n.StartsWith("Erp.Modules."))
                .Should().BeEmpty($"{contracts.GetName().Name} must not depend on any module");
    }

    [Fact]
    public void Only_the_host_references_shared_infrastructure()
    {
        foreach (var module in Modules.Concat(ContractsAssemblies))
            module.GetReferencedAssemblies().Select(a => a.Name)
                .Should().NotContain("Erp.Shared.Infrastructure",
                    $"{module.GetName().Name} must code against BuildingBlocks interfaces, never their implementations");
    }

    [Fact]
    public void A_module_exposes_only_its_entry_class()
    {
        foreach (var module in Modules)
        {
            var publicTypes = module.GetExportedTypes()
                // EF generates migrations as public classes.
                .Where(t => !(t.Namespace ?? "").EndsWith(".Migrations"))
                .Select(t => t.FullName);

            publicTypes.Should().ContainSingle($"{module.GetName().Name} should expose only its AddXModule entry class")
                .Which.Should().EndWith("Module");
        }
    }

    [Fact]
    public void Internals_are_shared_only_with_the_host_and_tests()
    {
        foreach (var module in Modules)
        {
            var friends = module.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName);

            friends.Should().OnlyContain(f => f == "Erp.Api" || f.EndsWith("Tests"),
                $"{module.GetName().Name} may grant InternalsVisibleTo to Erp.Api (Mediator generator) and test projects — never another module");
        }
    }

    [Fact]
    public void The_domain_does_not_depend_on_data_access_or_web()
    {
        string[] forbidden = ["Microsoft.EntityFrameworkCore", "Npgsql", "Dapper", "Microsoft.AspNetCore", "Carter"];

        foreach (var module in Modules)
        {
            var domainTypes = module.GetTypes().Where(t => (t.Namespace ?? "").EndsWith(".Domain"));
            foreach (var type in domainTypes)
            {
                var used = type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Select(f => f.FieldType)
                    .Concat(type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .SelectMany(c => c.GetParameters()).Select(p => p.ParameterType))
                    .Concat(type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                        .SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)));

                used.Select(t => t.Assembly.GetName().Name!)
                    .Where(a => forbidden.Any(a.StartsWith))
                    .Should().BeEmpty($"{type.FullName} is domain code and must stay free of data access and web types");
            }
        }
    }
}
```

Each test is one rule from the course, and each failure message says which rule broke and where. Try it: add
a project reference from Sales to the Inventory *module*, and the first test fails with "Erp.Modules.Sales may
reference another module's .Contracts only".

## Mistakes to avoid

- Testing business rules through HTTP. Slow, and a failure could be anywhere. Test rules in unit tests.
- Sharing your development database with the tests.
- Letting integration tests run in parallel against one database.
- Testing races with `Thread.Sleep` and hope. Start both at once with `Task.WhenAll`.
- A test that has never failed. Break the code it guards, once, and watch it go red.

## Let's run it!

```bash
dotnet test
```

```
Passed!  - Failed: 0, Passed: 22, ... Erp.Modules.Sales.UnitTests.dll
Passed!  - Failed: 0, Passed:  6, ... Erp.Architecture.Tests.dll
Passed!  - Failed: 0, Passed: 24, ... Erp.Integration.Tests.dll
```

Then prove the least-privilege accounts from lesson 13 are enough for everything the app does. Create the
roles against a fresh database, and run the suite as `erp_app`:

```bash
PGPASSWORD=erp_dev_only "/c/Program Files/PostgreSQL/18/bin/psql.exe" -h localhost -U postgres \
  -v db=erp_roles_check -v owner_password=owner_dev_only -v app_password=app_dev_only \
  -f deploy/postgres/create-roles.sql

ERP_TEST_DB="Host=localhost;Database=erp_roles_check;Username=erp_app;Password=app_dev_only" \
ERP_TEST_DB_OWNER="Host=localhost;Database=erp_roles_check;Username=erp_owner;Password=owner_dev_only" \
dotnet test tests/Erp.Integration.Tests
```

All 24 pass, with the app never holding more than row-level rights.

## Let's check!

1. Which bugs can't a unit test catch, and why?
2. Why is the test database dropped at the start of every run?
3. How does the race test make two reservations happen at the same time?
4. How would you know the lost-update test actually protects anything?
5. Why does the lockout test use a throwaway user?
6. Name two architecture rules the tests enforce, and what each one protects.

And that's where this lesson ends. Every rule, every race and every wall is now proven automatically, every
time you run `dotnet test`. Next time, we put the app on a real Windows server, behind IIS. Good luck,
developer!

---

← [Lesson 13 — Security hardening](13-security-hardening.md) · Next: [Lesson 15 — Deploy to IIS](15-deploy-iis.md) →
