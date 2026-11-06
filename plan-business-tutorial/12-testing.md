# Lesson 12 — Testing: Proving It All Works, Automatically

Good day, developer! We've had unit tests since [lesson 03](03-domain-model.md), and they prove the business
rules. But some bugs can't live in a unit test: two requests racing for the last units of stock, a transaction
that doesn't really span two modules, a validator that silently stopped running. Today we'll catch those, by
running the whole API against a real PostgreSQL database. We'll write tests for the architecture itself, so
nobody can quietly knock down a module wall. And we'll drive a real browser through the real screens. About
two and a half hours. Are you ready? Let's begin!

## What we'll build

| Test project | What it proves | Speed |
|---|---|---|
| `Erp.Modules.Sales.UnitTests` (from lesson 03) | Every business rule | Milliseconds, no database |
| `Erp.Integration.Tests` | Real requests through the real API and a real database: auth, workflow, races, outbox | Seconds |
| `Erp.Architecture.Tests` | The module rules: who may reference whom, what may be public, that every event is handled | Milliseconds |
| `Erp.Web.UiTests` | A real browser clicking through the real screens | Seconds, against a running app |

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

### And the screens, once

A test that drives a browser is slow and needs a running app, so we keep just a few: one walk through the
main business flow, and one that opens every page and checks nothing crashed. They catch what no API test
can, like a page that still expects a field the API renamed.

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

    public const string Password = "Password123!";

    public CustomWebAppFactory() => DropDatabase(TestConnectionString);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:ErpDb", TestConnectionString);
        builder.UseSetting("Outbox:PollIntervalMs", "200");
    }

    public sealed record Tokens(string AccessToken, string RefreshToken);

    public async Task<Tokens> LoginAsync(HttpClient client, string email, string password = Password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
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

    public static void DropDatabase(string connectionString)
    {
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres", Pooling = false };
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
| A fast outbox (200 ms) | The outbox test doesn't wait two seconds per check |
| `ERP_TEST_DB` | Point the suite at another server without touching the code |
| Bearer tokens, not cookies | A test is a program, not a browser; lesson 05 gave programs tokens |

`DropDatabase` is public and takes the connection string, because one test (in
[lesson 22](22-docker.md)) needs a second, empty database of its own.

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

    private async Task<bool> ReserveInOwnCommittedTransaction(long itemId, decimal qty)
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

    private static async Task<decimal> GetReserved(long itemId)
    {
        await using var conn = new NpgsqlConnection(CustomWebAppFactory.TestConnectionString);
        return await conn.ExecuteScalarAsync<decimal>(
            "SELECT reserved FROM inventory.item WHERE id = @itemId", new { itemId });
    }

    private static async Task SetReserved(long itemId, decimal reserved)
    {
        await using var conn = new NpgsqlConnection(CustomWebAppFactory.TestConnectionString);
        await conn.ExecuteAsync(
            "UPDATE inventory.item SET reserved = @reserved WHERE id = @itemId", new { itemId, reserved });
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
using Erp.Shared.BuildingBlocks;
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

        aliceOrder.SetLines(await aliceDb.ResolveLinesAsync([(1, 3)], default), Roles.SalesRep);
        bobOrder.SetLines(await bobDb.ResolveLinesAsync([(2, 1)], default), Roles.SalesRep);

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
        order.SetLines(await db.ResolveLinesAsync([(1, 1)], default), Roles.SalesRep);
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }
}
```

Delete the `SaveChangesAsync` override from [lesson 10](10-update-order.md) and run this test: it fails,
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
    await rep.PostAsync($"/api/sales-orders/{id}/submit", null);
    (await StatusOf(rep, id)).Should().Be("PendingApproval");

    (await manager.PostAsync($"/api/sales-orders/{id}/approve", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

    (await StatusOf(rep, id)).Should().Be("Approved");
}
```

`ClientFor(...)` signs in through the real `/api/auth/login` and returns an `HttpClient` carrying the token. The
full set lives in the reference build; write yours in the same style:

| File | Its tests prove |
|---|---|
| [SalesOrderWorkflowTests.cs](../course-validation/business/tests/Erp.Integration.Tests/SalesOrderWorkflowTests.cs) | Auto-approval; manager approval; no self-approval; reps can't approve (403); reject releases stock; pending orders count against credit; blocked and over-limit customers (422); bare search works; a page past the end keeps the real total; **an order with no lines is rejected (the guard for `includeInternalTypes`)** |
| [AuthTests.cs](../course-validation/business/tests/Erp.Integration.Tests/AuthTests.cs) | Wrong password is 401; `/api/auth/me` returns the role; refresh works; `/api/auth/register` is 404; **five wrong passwords lock an account**; sign-out-everywhere kills refresh tokens |
| [PlatformTests.cs](../course-validation/business/tests/Erp.Integration.Tests/PlatformTests.cs) | **An approval event is delivered through the outbox**; `/sales-orders` serves the app; an unknown `/api/...` is a 404, not the app's page |
| [BusinessFlowTests.cs](../course-validation/business/tests/Erp.Integration.Tests/BusinessFlowTests.cs) | **A rep sees only their own orders**, and gets a 404 for anyone else's |

The reference build's files hold more tests than you can write today: the health checks, security headers,
shipping, invoicing and the rest arrive with their lessons, and each of those lessons adds its tests to
these files.

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
using Erp.Shared.BuildingBlocks;
using Mediator;

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

    // Raise an event only with its first handler. An event nobody handles is a feature that silently does nothing.
    [Fact]
    public void Every_event_has_a_handler()
    {
        var events = Modules.Concat(ContractsAssemblies)
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IDomainEvent).IsAssignableFrom(t));

        var handled = Modules.SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetInterfaces())
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>))
            .Select(i => i.GetGenericArguments()[0])
            .ToHashSet();

        events.Where(e => !handled.Contains(e)).Select(e => e.FullName).Should().BeEmpty("every event needs at least one handler");
    }
}
```

The last test turns lesson 11's rule ("raise an event only in the change that adds its first handler") into
something the build checks. The Mediator generator's MSG0005 warning catches the same mistake, but a
warning can scroll past; a red test can't.

Each test is one rule from the course, and each failure message says which rule broke and where. Try it: add
a project reference from Sales to the Inventory *module*, and the first test fails with "Erp.Modules.Sales may
reference another module's .Contracts only".

## 8. A real browser

**Playwright** (Microsoft, MIT) drives a real browser from C#: open a page, type, click, wait for text.

```bash
dotnet new classlib -n Erp.Web.UiTests -o tests/Erp.Web.UiTests
rm tests/Erp.Web.UiTests/Class1.cs
dotnet sln add tests/Erp.Web.UiTests
dotnet add tests/Erp.Web.UiTests package Microsoft.NET.Test.Sdk --version 17.14.1
dotnet add tests/Erp.Web.UiTests package xunit --version 2.9.3
dotnet add tests/Erp.Web.UiTests package xunit.runner.visualstudio --version 3.1.4
dotnet add tests/Erp.Web.UiTests package AwesomeAssertions --version 9.6.0
dotnet add tests/Erp.Web.UiTests package Microsoft.Playwright --version 1.64.1
```

As before, delete the `<PropertyGroup>`, then add `<IsPackable>false</IsPackable>` and
`<Using Include="Xunit" />`. Notice it references no project at all: it only knows the app's address.

These tests need a running app, so they're skipped unless you say where it is:

```csharp
// tests/Erp.Web.UiTests/BusinessFlowUiTests.cs
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.Playwright;

namespace Erp.Web.UiTests;

// Runs only against a running app with the development seed: set ERP_UI_URL (e.g. http://localhost:5264).
// ERP_UI_SCREENSHOTS, if set, is a folder that receives a screenshot of every step.
public sealed class UiFactAttribute : FactAttribute
{
    public UiFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ERP_UI_URL")))
            Skip = "Set ERP_UI_URL to a running ERP to run browser tests.";
    }
}

public class BusinessFlowUiTests : IAsyncLifetime
{
    private static readonly string BaseUrl = Environment.GetEnvironmentVariable("ERP_UI_URL")?.TrimEnd('/') ?? "";
    private static readonly string? Screenshots = Environment.GetEnvironmentVariable("ERP_UI_SCREENSHOTS");
    private IPlaywright _playwright = default!;
    private IBrowser _browser = default!;
    private int _shot;

    public async Task InitializeAsync()
    {
        if (BaseUrl.Length == 0) return;
        _playwright = await Playwright.CreateAsync();
        // The Edge (or Chrome) already on the machine: nothing to download.
        _browser = await _playwright.Chromium.LaunchAsync(new() { Channel = OperatingSystem.IsWindows() ? "msedge" : "chrome" });
    }

    public async Task DisposeAsync()
    {
        if (BaseUrl.Length == 0) return;
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }

    // Each person gets their own browser context: their own cookie, like their own computer.
    private async Task<IPage> SignIn(string email)
    {
        var context = await _browser.NewContextAsync(new() { BaseURL = BaseUrl, ViewportSize = new() { Width = 1366, Height = 820 } });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/login");
        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Password").FillAsync("Password123!");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await page.GetByText("Good day,").WaitForAsync();
        return page;
    }

    private async Task Shot(IPage page, string name)
    {
        if (Screenshots is null) return;
        Directory.CreateDirectory(Screenshots);
        await page.ScreenshotAsync(new() { Path = Path.Combine(Screenshots, $"{++_shot:00}-{name}.png"), FullPage = true });
    }

    private static async Task Pick(IPage page, string label, string search, string option)
    {
        await page.GetByLabel(label).First.FillAsync(search);
        await page.GetByText(option).First.ClickAsync();
    }

    [UiFact]
    public async Task From_order_to_approval_through_the_screens()
    {
        // A rep writes an order over ₱50,000 for Epsilon Retail and submits it.
        var rep = await SignIn("rep@erp.local");
        await Shot(rep, "rep-home");
        await rep.GotoAsync("/sales-orders/new");
        await Pick(rep, "Customer", "Epsilon", "Epsilon Retail");
        await Pick(rep, "Item", "Rack", "GEN-003 · Server Rack");
        await rep.GetByRole(AriaRole.Button, new() { Name = "Save draft" }).ClickAsync();
        await rep.WaitForURLAsync(new Regex(@"/sales-orders/\d+$"));
        var orderUrl = new Uri(rep.Url).AbsolutePath;
        await rep.GetByRole(AriaRole.Button, new() { Name = "Submit" }).ClickAsync();
        await rep.Locator(".mud-chip").Filter(new() { HasText = "Pending approval" }).WaitForAsync();
        await Shot(rep, "rep-submitted");

        // The manager approves it.
        var manager = await SignIn("manager@erp.local");
        await manager.GotoAsync(orderUrl);
        await manager.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();
        await manager.Locator(".mud-chip").Filter(new() { HasText = "Approved" }).WaitForAsync();
        await Shot(manager, "manager-approved");
    }

    [UiFact]
    public async Task Every_page_opens_without_an_error()
    {
        var admin = await SignIn("admin@erp.local");
        string[] pages = ["/sales-orders", "/sales-orders/new"];

        foreach (var path in pages)
        {
            await admin.GotoAsync(path);
            await admin.Locator("h4, h5").First.WaitForAsync();
            await admin.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await Shot(admin, path.Trim('/').Replace('/', '-'));

            (await admin.Locator("#blazor-error-ui").IsVisibleAsync()).Should().BeFalse($"{path} crashed the app");
            (await admin.Locator(".mud-alert-filled-error, .mud-snackbar.mud-alert-filled-error").CountAsync())
                .Should().Be(0, $"{path} showed an error");
        }
    }
}
```

- **It finds things the way a person does**: by label ("Customer"), by button name ("Submit"), by text on
  screen. So it breaks when the screen breaks, not when a CSS class changes.
- **Each person gets their own browser context**: their own cookies, so the rep and the manager are really
  two different sign-ins.
- **It uses the browser you already have** (`Channel = "msedge"` on Windows), so there's nothing to
  download. On Linux, install Chrome, or run `pwsh bin/Debug/net10.0/playwright.ps1 install chromium` once.
- **`#blazor-error-ui`** is the bar from lesson 01's `index.html`. If it's visible, a page threw.

Every later lesson that adds a screen adds its address to `pages`, and the lessons that ship and bill an
order add their steps to the flow test, until it walks from a new order to a paid invoice.

## Mistakes to avoid

- Testing business rules through HTTP. Slow, and a failure could be anywhere. Test rules in unit tests.
- Sharing your development database with the tests.
- Letting integration tests run in parallel against one database.
- Testing races with `Thread.Sleep` and hope. Start both at once with `Task.WhenAll`.
- A test that has never failed. Break the code it guards, once, and watch it go red.
- Dozens of browser tests. They're slow and fragile; keep a few that walk the main flows.

## Let's run it!

```bash
dotnet test
```

```
Passed!  - Failed: 0, Passed: 20, ... Erp.Modules.Sales.UnitTests.dll
Passed!  - Failed: 0, Passed:  7, ... Erp.Architecture.Tests.dll
Passed!  - Failed: 0, Passed: .., ... Erp.Integration.Tests.dll
Skipped! - Failed: 0, Skipped: 2, ... Erp.Web.UiTests.dll
```

The integration count is however many workflow and auth tests you wrote. The UI tests skip, because no app
is running for them. So start one, and point them at it:

```bash
dotnet run --project src/Erp.Api &
ERP_UI_URL=http://localhost:5264 ERP_UI_SCREENSHOTS=./screens dotnet test tests/Erp.Web.UiTests
```

An Edge window you never see (it runs *headless*) signs in as the rep, creates and submits an order, then
signs in as the manager and approves it. Open the `screens` folder afterwards: every step is a picture.
The UI tests use your development database, so they add a few orders to it each run. That's what the dev
seed is for.

## Let's check!

1. Which bugs can't a unit test catch, and why?
2. Why is the test database dropped at the start of every run?
3. How does the race test make two reservations happen at the same time?
4. How would you know the lost-update test actually protects anything?
5. Why does the lockout test use a throwaway user?
6. Name two architecture rules the tests enforce, and what each one protects.
7. Why are there only a few browser tests, and how do they find a button?

And that's where this lesson ends. Every rule, every race, every wall and the main screens are now proven
automatically, every time you run `dotnet test`. Next time, we make the running app visible: traces,
metrics, logs and health checks, so you can see what it's doing when something goes wrong. Good luck,
developer!

---

← [Lesson 11 — Events and the outbox](11-events-outbox.md) · Next: [Lesson 13 — Observability](13-observability.md) →
