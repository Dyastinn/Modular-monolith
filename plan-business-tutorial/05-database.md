# Lesson 05 — The Database: Giving the Tickets Module Its Memory

Good day, developer! Last time, we wrote the Ticket's rules in plain C#, and proved them without a database.
Today we give them a memory. We install PostgreSQL on your own computer, teach EF Core to save and load a
`Ticket` with its conversation, build the `tickets` schema with a migration, put three sample tickets in it, and
write the first tests that talk to a real database. It takes about two hours. Are you ready? Let's begin!

## What we'll build

| Piece | What it is |
|---|---|
| **PostgreSQL**, installed natively | The database server, on Windows, macOS, Fedora or Ubuntu, with a development login |
| **`TicketsDbContext`** | EF Core's map between `Ticket` and the tables `ticket` and `ticket_message` |
| **The first migration** | `InitialCreate`: the `tickets` schema as code you can read, review and replay |
| **`TicketsDatabase`** | What the host asks the module to do at startup: migrate, and in development, seed sample tickets |
| **Database tests** | The first integration tests that use the database: the schema, the mapping, and a ticket's round trip |

## Why we build it this way

### The pain: a program that forgets

Right now, every `Ticket` lives in memory. Stop the app and it's gone. So we need a database, and the first
question is how the code talks to it. The obvious way is to write SQL by hand for every save:

```csharp
command.CommandText = "INSERT INTO tickets (subject, status, ...) VALUES (@subject, @status, ...)";
```

Then add a message: another INSERT. Change the priority: an UPDATE of the right columns, and only those. Add
a column next month: find every statement that needs it. It works, and it's a lot of text that the compiler
can't check.

### The fix: let EF Core write the saving

**Entity Framework Core** (EF Core) is Microsoft's library for this. You describe how your classes map to
tables, once. Then you work with objects: load a `Ticket`, call its methods, save. EF Core remembers every
object it loaded and, on save, writes only what changed. That is **change tracking**.

It also gives us **migrations**: a change to the schema is a small file of C# that says "create this table, add
that column", and a table in the database remembers which ones have run. A new developer, a test machine and
production all reach the same schema by replaying the same files.

Now, let's be honest about the cost here: EF Core does a lot behind your back, and change tracking costs memory
and time that a hand-written statement doesn't. For *writing* one ticket at a time, that's a bargain. For
*reading* long lists it isn't, so in lesson 08 reads switch to a lighter tool, Dapper. That's
two data-access tools in one product, on purpose: each used where it's strong.

### One schema per module

PostgreSQL has **schemas**: named folders inside a database. Each module gets its own: Tickets owns the
`tickets` schema, and later Identity owns `identity`. The module that owns a table is the only one that reads
or changes it, which is the island rule, now in the database. Each module also keeps its own migration history,
inside its own schema. The schema's name is the module's name in lower case.

### Why PostgreSQL

It's free, permissively licensed (the PostgreSQL license is MIT-like), runs the same everywhere, and has the
features this product grows into: full-text search for tickets and articles, and strong transactions. Version 16
or newer works; we develop on 18.

### Why install it natively, for now

Later lessons run the database in a container, which keeps your machine clean. Containers need one more tool and
one more idea, so we save them for lesson 12, where you'll see exactly what they fix. Until
then, the database is an ordinary program on your computer.

## 1. Install PostgreSQL

Pick the section for your computer. You only need one.

### On Windows 10 or 11

In PowerShell:

```powershell
winget install --id PostgreSQL.PostgreSQL.18 -e
```

The installer asks for a password for the database's administrator, called `postgres`. Choose one and keep it.
Accept the other defaults (port 5432). Then add its tools to Git Bash's path, so `psql` works there. In Git Bash:

```bash
echo 'export PATH="$PATH:/c/Program Files/PostgreSQL/18/bin"' >> ~/.bashrc
```

Close Git Bash and open a new one.

### On macOS

```bash
brew install postgresql@18
brew services start postgresql@18
echo 'export PATH="/opt/homebrew/opt/postgresql@18/bin:$PATH"' >> ~/.zshrc
```

Open a new Terminal after the last line. (On an Intel Mac, Homebrew lives in `/usr/local` instead of
`/opt/homebrew`.)

### On Fedora Linux

```bash
sudo dnf install postgresql-server
sudo postgresql-setup --initdb
sudo systemctl enable --now postgresql
```

### On Ubuntu

```bash
sudo apt-get install -y postgresql
```

Ubuntu starts the server by itself. Its version depends on your release; 16 or newer is what we need.

### Check that it is running

```bash
psql --version
```

```
psql (PostgreSQL) 18.1
```

Your number may differ. `psql` is PostgreSQL's own command-line client, and you'll use it to look inside the
database.

### Create your development login

The application should never connect as the all-powerful administrator. We make a login of its own, allowed to
create databases (the app creates its own on first run) and nothing more. The password is a development one,
and it is meant to be public: never use it anywhere that matters.

On Linux, ask the administrator account to do it:

```bash
sudo -u postgres psql -c "CREATE ROLE yasda_dev LOGIN CREATEDB PASSWORD 'yasda_dev_only';"
```

On Windows and macOS, connect as the administrator you made (on macOS, `postgres` is your own user name, and
there's no password to type):

```bash
psql -U postgres -c "CREATE ROLE yasda_dev LOGIN CREATEDB PASSWORD 'yasda_dev_only';"
```

```
CREATE ROLE
```

Now prove the new login works over the network port, the way the app will use it:

```bash
psql "host=localhost dbname=postgres user=yasda_dev password=yasda_dev_only" -c "SELECT 1"
```

```
 ?column?
----------
        1
(1 row)
```

If Fedora answers "Ident authentication failed", the server is set to trust the operating system's user name
for local network connections. Open `/var/lib/pgsql/data/pg_hba.conf` as an administrator, change `ident` to
`scram-sha-256` on the two `host` lines, and run `sudo systemctl restart postgresql`.

## 2. The packages

```bash
dotnet add src/Modules/Tickets/Yasda.Modules.Tickets package Npgsql.EntityFrameworkCore.PostgreSQL --version 10.0.3
dotnet add src/Modules/Tickets/Yasda.Modules.Tickets package EFCore.NamingConventions --version 10.0.1
dotnet add src/Modules/Tickets/Yasda.Modules.Tickets package Microsoft.EntityFrameworkCore.Relational --version 10.0.14
dotnet add src/Modules/Tickets/Yasda.Modules.Tickets package Microsoft.EntityFrameworkCore.Design --version 10.0.14
dotnet add src/Yasda.Api package Microsoft.EntityFrameworkCore --version 10.0.14
dotnet add src/Yasda.Api package Microsoft.EntityFrameworkCore.Design --version 10.0.14
dotnet add src/Shared/Yasda.Shared.Infrastructure package Npgsql --version 10.0.3
```

| Package | What it's for | License |
|---|---|---|
| `Npgsql.EntityFrameworkCore.PostgreSQL` | EF Core's PostgreSQL driver (brings **Npgsql**, the .NET driver itself) | PostgreSQL license |
| `EFCore.NamingConventions` | Names every table and column in `snake_case` (`requester_id`, not `RequesterId`), the PostgreSQL habit | Apache-2.0 |
| `Microsoft.EntityFrameworkCore.Relational` | Pinned here for one reason: the PostgreSQL driver pulls in an older EF Core, and without a direct reference the build warns that two versions collide | MIT |
| `Microsoft.EntityFrameworkCore.Design` | What the `dotnet ef` tool needs to read your model and write migrations | MIT |
| `Microsoft.EntityFrameworkCore` | The core, referenced by the host too, so every project in the solution uses one version | MIT |
| `Npgsql` | In `Yasda.Shared.Infrastructure`, the one place that may talk to the server directly: it creates the database if it's missing | PostgreSQL license |

Open the `.csproj` files of Tickets and the host. Each `Microsoft.EntityFrameworkCore.Design` entry should carry
`PrivateAssets`, meaning "I need this to build, but my users don't": a library that needs the design tools
shouldn't force them on everyone who references it. If `dotnet add` wrote it as a block of child lines, collapse
it to one line:

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" PrivateAssets="all" />
```

The design package is the one that surprises people: the `dotnet ef` tool wants an *executable* project to start
(here, the host), and that project must reference the design package too.

One more tool: `dotnet ef` itself, installed as a **local tool**, meaning it belongs to this solution, not to your
whole machine:

```bash
dotnet new tool-manifest
dotnet tool install dotnet-ef --version 10.0.14
```

The first command writes `dotnet-tools.json`, a list of the tools this solution uses. Commit it with the code, and
whoever clones the repository gets the same tool version with `dotnet tool restore`.

Finally, the first logging call is coming (in step 4), and the analyzers from lesson 01 will ask for
source-generated logging, which pays off only in code that logs thousands of times a second. Ours logs once at
startup. Add two lines to `.editorconfig`, with the reason beside them, like lesson 01's CA1716:

```ini
# CA1848, CA1873: source-generated logging pays off on hot paths. Ours log at startup and on failures.
dotnet_diagnostic.CA1848.severity = none
dotnet_diagnostic.CA1873.severity = none
```

## 3. The map: `TicketsDbContext`

A **DbContext** is EF Core's main class: one instance is one conversation with the database. It holds the change
tracking, and a `DbSet` per kind of thing you load and save.

```csharp
// src/Modules/Tickets/Yasda.Modules.Tickets/Infrastructure/TicketsDbContext.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Yasda.Modules.Tickets.Domain;

namespace Yasda.Modules.Tickets.Infrastructure;

internal sealed class TicketsDbContext(DbContextOptions<TicketsDbContext> options) : DbContext(options)
{
    public const string Schema = "tickets";

    public DbSet<Ticket> Tickets => Set<Ticket>();

    // Shared by the app and `dotnet ef`, so the two can't drift apart.
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString)
    {
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", Schema));
        options.UseSnakeCaseNamingConvention();
        return options;
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configuration)
    {
        configuration.Properties<TicketId>().HaveConversion<TicketIdConverter>();
        configuration.Properties<UserId>().HaveConversion<UserIdConverter>();
        // Statuses and priorities are stored by name: readable in SQL, and safe if the enum is ever reordered.
        configuration.Properties<TicketStatus>().HaveConversion<string>().HaveMaxLength(20);
        configuration.Properties<TicketPriority>().HaveConversion<string>().HaveMaxLength(20);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Schema);

        builder.Entity<Ticket>(e =>
        {
            e.ToTable("ticket", t =>
            {
                t.HasCheckConstraint("ck_ticket_status", "status IN ('New', 'Open', 'Pending', 'Resolved', 'Closed', 'Cancelled')");
                t.HasCheckConstraint("ck_ticket_priority", "priority IN ('Low', 'Normal', 'High', 'Critical')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.Subject).HasMaxLength(Ticket.SubjectMaxLength);
            e.Property(x => x.RequesterName).HasMaxLength(100);
            e.Property(x => x.AssigneeName).HasMaxLength(100);
            e.Property(x => x.CancelReason).HasMaxLength(Ticket.ReasonMaxLength);
            e.HasIndex(x => new { x.Status, x.UpdatedUtc });
            e.HasIndex(x => x.RequesterId);
            e.HasIndex(x => x.AssigneeId);
            e.Ignore(x => x.IsFinished);

            e.OwnsMany(x => x.Messages, m =>
            {
                m.ToTable("ticket_message");
                m.WithOwner().HasForeignKey("TicketId");
                m.HasKey(x => x.Id);
                m.Property(x => x.Id).UseIdentityColumn();
                m.Property(x => x.AuthorName).HasMaxLength(100);
                m.Property(x => x.Body).HasMaxLength(Ticket.MessageMaxLength);
            });
            e.Navigation(x => x.Messages).HasField("_messages").UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }
}

internal sealed class TicketIdConverter() : ValueConverter<TicketId, long>(v => v.Value, v => new TicketId(v));
internal sealed class UserIdConverter() : ValueConverter<UserId, long>(v => v.Value, v => new UserId(v));

// For `dotnet ef migrations add`; never connects.
internal sealed class TicketsDbContextFactory : IDesignTimeDbContextFactory<TicketsDbContext>
{
    public TicketsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TicketsDbContext>();
        TicketsDbContext.Configure(options, "Host=localhost;Database=yasda_design_time");
        return new TicketsDbContext(options.Options);
    }
}
```

Let's walk through it:

- **`Configure`** says "PostgreSQL, snake_case names, and keep this module's migration history inside the
  `tickets` schema". It's a static method on purpose: the app and the `dotnet ef` tool both call it, so they can
  never disagree.
- **`ConfigureConventions`** teaches EF Core how to store our own types. `TicketId` becomes a `bigint` through a
  small converter. Statuses and priorities are saved **by name** (`'Open'`, not `1`): the table reads
  well in SQL, and reordering the C# enum later can't silently change what old rows mean.
- **`OnModelCreating`** maps `Ticket` to the table `ticket`, with the lengths we already decided and two
  **check constraints**: the database itself refuses a status or priority we don't know, even if some future code
  forgets. Defense in depth.
- **`UseIdentityColumn`** makes the database hand out ticket numbers: 1, 2, 3.
- **`e.Ignore(x => x.IsFinished)`** keeps a computed property out of the table.
- **`OwnsMany`** says the messages belong to their ticket: they live in `ticket_message`, with a `ticket_id`
  that's deleted along with the ticket. This is the aggregate rule from lesson 04, now in tables.
- **`HasField("_messages")`** tells EF Core to fill the private list directly. The ticket's public `Messages`
  can then stay read-only, as the domain wants.

EF Core builds objects by calling a constructor, and it needs one it can call with nothing. `Ticket` has its
private `Ticket()` already. `TicketMessage` doesn't, so add one above its public constructor:

```csharp
// src/Modules/Tickets/Yasda.Modules.Tickets/Domain/TicketMessage.cs (add above the internal constructor)
    private TicketMessage() { }
```

Nothing else can call it, so the rules from lesson 04 still hold: the only way to make a message is through a
ticket.

## 4. What the host asks of a module

The host needs two things from each module's database: bring its schema up to date, and, in development, fill
it with sample data. But a module's classes are `internal`, so the host can't call them. The way through is a
small interface in the shared project, which the module implements:

```csharp
// src/Shared/Yasda.Shared.BuildingBlocks/Abstractions/Data.cs
namespace Yasda.Shared.BuildingBlocks;

// What the host needs from a module's database, without seeing inside it: bring the schema up to date, and (in
// development) put a few sample rows in.
public interface IModuleDatabase
{
    string Module { get; }
    Task MigrateAsync(CancellationToken ct);
    Task SeedDevelopmentDataAsync(CancellationToken ct);
}
```

And the module's side of it, where we also build the three sample tickets, through the domain like real ones:

```csharp
// src/Modules/Tickets/Yasda.Modules.Tickets/Infrastructure/TicketsDatabase.cs
using Microsoft.EntityFrameworkCore;
using Yasda.Modules.Tickets.Domain;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Infrastructure;

internal sealed class TicketsDatabase(TicketsDbContext db, TimeProvider time) : IModuleDatabase
{
    public string Module => "Tickets";

    public async Task MigrateAsync(CancellationToken ct)
    {
        await db.Database.MigrateAsync(ct);
    }

    // A few tickets to click through, built through the domain like real ones. The people are stand-ins (ids 2 and 3)
    // until Tickets can ask Identity who people are (lesson 07).
    public async Task SeedDevelopmentDataAsync(CancellationToken ct)
    {
        if (await db.Tickets.AnyAsync(ct)) return;

        var requester = new UserId(2);
        var agent = new UserId(3);
        var now = time.GetUtcNow().UtcDateTime;

        var printer = Ticket.Create("Printer on the 3rd floor won't print",
            "It shows \"paper jam\" but there's no paper stuck. Twenty people share it.", TicketPriority.High, requester, "Rita Cruz", now.AddHours(-30));

        var vpn = Ticket.Create("Can't connect to the VPN from home",
            "It says my certificate expired. I need it for Monday's report.", TicketPriority.Normal, requester, "Rita Cruz", now.AddHours(-6));
        vpn.Take(agent, "Alan Reyes", now.AddHours(-5));
        vpn.Reply(agent, "Alan Reyes", "Which computer are you using: the company laptop or your own?", isInternal: false, authorIsStaff: true, now.AddHours(-5));
        vpn.Reply(agent, "Alan Reyes", "Certificates were renewed last week; this may be the first of many.", isInternal: true, authorIsStaff: true, now.AddHours(-5));
        vpn.MarkPending(now.AddHours(-5));

        var mouse = Ticket.Create("New mouse, please", "My scroll wheel stopped working.", TicketPriority.Low, requester, "Rita Cruz", now.AddDays(-3));
        mouse.Take(agent, "Alan Reyes", now.AddDays(-3));
        mouse.Reply(agent, "Alan Reyes", "Left one on your desk. Shout if it doesn't work.", isInternal: false, authorIsStaff: true, now.AddDays(-2));
        mouse.Resolve(now.AddDays(-2));

        db.Tickets.AddRange(printer, vpn, mouse);
        await db.SaveChangesAsync(ct);
    }
}
```

- **`MigrateAsync`** applies every migration that hasn't run yet. Running it twice is harmless.
- **`SeedDevelopmentDataAsync`** adds three tickets, but only if there are none: **idempotent**, which means
  doing it again changes nothing. We rely on that every time the app starts.
- **Seed data goes through `Ticket.Create` and its methods**, not through raw INSERTs. If the rules change, the
  sample data is checked by them too.
- **The people are stand-ins** (ids 2 and 3, with their names written in). Identity doesn't exist yet. In lesson 07,
  when Tickets first needs to know who somebody is, this method learns to ask Identity for real ids.

The module registers its pieces. Replace `TicketsModule.cs` from lesson 03 with:

```csharp
// src/Modules/Tickets/Yasda.Modules.Tickets/TicketsModule.cs
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Routing;
using Yasda.Modules.Tickets.Features;
using Yasda.Modules.Tickets.Infrastructure;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets;

public static class TicketsModule
{
    public static IServiceCollection AddTicketsModule(this IServiceCollection services)
    {
        // One DbContext per request. Its connection string comes from configuration when the context is first needed.
        services.AddDbContext<TicketsDbContext>((provider, options) =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            TicketsDbContext.Configure(options, configuration.GetConnectionString("YasdaDb")!);
        });
        services.AddScoped<IModuleDatabase, TicketsDatabase>();

        // Handlers: plain classes, injected straight into the endpoints that use them.
        services.AddScoped<PingHandler>();

        // Without includeInternalTypes no validator is found, and validation silently stops.
        services.AddValidatorsFromAssembly(typeof(TicketsModule).Assembly, includeInternalTypes: true);
        return services;
    }

    public static IEndpointRouteBuilder MapTicketsEndpoints(this IEndpointRouteBuilder api)
    {
        PingEndpoint.Map(api);
        return api;
    }
}
```

`AddDbContext` registers `TicketsDbContext` as **scoped**: one per request, as with handlers. The second
argument is a function that runs when the context is first needed, and reads the connection string from
configuration at that moment.

Now the host. The startup code creates the database if it's missing, applies the migrations, and seeds:

```csharp
// src/Yasda.Api/DatabaseStartup.cs
using Yasda.Shared.BuildingBlocks;
using Yasda.Shared.Infrastructure;

namespace Yasda.Api;

// Off by default (appsettings.json); on in Development and tests. Production will apply migrations as a
// deployment step instead, so a bad migration can't take the site down on a routine restart.
internal static class DatabaseStartup
{
    public static async Task InitializeDatabaseAsync(this WebApplication app, CancellationToken ct = default)
    {
        var settings = app.Configuration.GetSection("Database");
        if (!settings.GetValue<bool>("MigrateOnStartup")) return;

        await PostgresDatabase.EnsureExistsAsync(app.Configuration.GetConnectionString("YasdaDb")!, ct);

        await using var scope = app.Services.CreateAsyncScope();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseStartup");
        var modules = scope.ServiceProvider.GetServices<IModuleDatabase>().ToList();

        foreach (var module in modules)
        {
            await module.MigrateAsync(ct);
            log.LogInformation("{Module} schema is up to date", module.Module);
        }

        if (!settings.GetValue<bool>("SeedDevelopmentData")) return;

        foreach (var module in modules)
            await module.SeedDevelopmentDataAsync(ct);
        log.LogInformation("Development data seeded");
    }
}
```

and a helper in the shared infrastructure project, the one place allowed to talk to the server directly, to
create the database:

```csharp
// src/Shared/Yasda.Shared.Infrastructure/Data/PostgresDatabase.cs
using Npgsql;

namespace Yasda.Shared.Infrastructure;

public static class PostgresDatabase
{
    // Creates the database named in the connection string if the server doesn't have it yet.
    public static async Task EnsureExistsAsync(string connectionString, CancellationToken ct = default)
    {
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres", Pooling = false };

        await using var conn = new NpgsqlConnection(admin.ConnectionString);
        await conn.OpenAsync(ct);

        await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", conn);
        exists.Parameters.AddWithValue("name", target.Database!);
        if (await exists.ExecuteScalarAsync(ct) is not null) return;

        // A database name can't be a parameter, so quote it as an identifier.
        var quoted = "\"" + target.Database!.Replace("\"", "\"\"") + "\"";
        await using var create = new NpgsqlCommand($"CREATE DATABASE {quoted}", conn);
        await create.ExecuteNonQueryAsync(ct);
    }
}
```

One thing to watch out for: a database name can't be a parameter in SQL, so the helper quotes it as an identifier
itself. The two flags that switch all this on are **settings**, not code. Replace `appsettings.json` with the safe
defaults (everything off, no connection string), and create `appsettings.Development.json` with what only
your machine needs:

```json
// src/Yasda.Api/appsettings.json
{
  "ConnectionStrings": {
    "YasdaDb": ""
  },
  "Database": {
    "MigrateOnStartup": false,
    "SeedDevelopmentData": false
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

```json
// src/Yasda.Api/appsettings.Development.json
{
  "ConnectionStrings": {
    "YasdaDb": "Host=localhost;Port=5432;Database=yasda;Username=yasda_dev;Password=yasda_dev_only"
  },
  "Database": {
    "MigrateOnStartup": true,
    "SeedDevelopmentData": true
  }
}
```

ASP.NET Core reads `appsettings.json`, then lays `appsettings.Development.json` over it when the environment is
Development (which `dotnet run` sets from `launchSettings.json`). In production, both flags stay off and the
connection string comes from the server's settings: a migration should be a deliberate step there, never a
side effect of restarting. Lesson 24 shows how.

Last, the two lines in `Program.cs`. Before:

```csharp
builder.Services.AddTicketsModule();
```

```csharp
var app = builder.Build();
```

After:

```csharp
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddTicketsModule();
```

```csharp
var app = builder.Build();

// Development and tests: create the database, apply the migrations, add sample tickets. Does nothing when off.
await app.InitializeDatabaseAsync();
```

`TimeProvider` is .NET's own "what time is it?" service. The seed asks it instead of calling `DateTime.UtcNow`,
for the same reason the domain does: so tests can choose the time. `TimeProvider.System` is the real clock.

## 5. The first migration

Ask EF Core to compare our map with an empty database and write down the difference:

```bash
dotnet ef migrations add InitialCreate \
  --project src/Modules/Tickets/Yasda.Modules.Tickets \
  --startup-project src/Yasda.Api \
  --output-dir Infrastructure/Migrations
```

```
Build started...
Build succeeded.
Done. To undo this action, use 'ef migrations remove'
```

Three files appear in `Infrastructure/Migrations`: the migration itself (`..._InitialCreate.cs`), a designer file
that EF Core uses, and `TicketsDbContextModelSnapshot.cs`, its picture of the model *now*. The next migration is
the difference between that picture and your code. Open the first one. The top of `Up`, trimmed:

```csharp
migrationBuilder.EnsureSchema(name: "tickets");

migrationBuilder.CreateTable(
    name: "ticket",
    schema: "tickets",
    columns: table => new
    {
        id = table.Column<long>(type: "bigint", nullable: false)
            .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
        subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
        status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
        // ...
    },
    constraints: table =>
    {
        table.PrimaryKey("pk_ticket", x => x.id);
        table.CheckConstraint("ck_ticket_status", "status IN ('New', 'Open', 'Pending', 'Resolved', 'Closed', 'Cancelled')");
        // ...
    });
```

It's plain, reviewable C#. Always read a generated migration before you commit it: it's the one place where
"EF Core guessed wrong" is cheap to catch.

One rule from now on: **a migration that has shipped is never edited.** To change the schema, change the map and
run `migrations add` again with a new name. Each lesson that changes a table adds its own migration, so your
history grows by one file per change. (The finished reference build squashes each module's history into a single
`InitialCreate`, because nobody has installed it yet. The schema it makes is the same as yours.)

Now build once more, from scratch: `dotnet build --no-incremental`. Two analyzer warnings appear in the migration
file, because EF Core writes code in its own style (a block-scoped namespace, an array in a call). We never edit
a generated file, so we tell the analyzers to leave it alone. Add this to the end of `.editorconfig`:

```ini
# EF Core writes migrations; they are generated code, kept exactly as generated.
[**/Migrations/*.cs]
generated_code = true
```

The generated migrations are `public` classes, and lesson 03's architecture test will notice: a module may expose
only its entry class. Migrations are the one exception, because EF Core generates them that way. Add the exception
to the test, with its reason. In `ModuleBoundaryTests`, before:

```csharp
var publicTypes = module.GetExportedTypes().Select(t => t.FullName);
```

After:

```csharp
// EF generates migrations as public classes.
var exported = module.GetExportedTypes().Where(t => !IsInNamespace(t, ".Migrations"));
var publicTypes = exported.Select(t => t.FullName);
```

`IsInNamespace` is the helper you added in lesson 04.

## 6. See it work

```bash
dotnet run --project src/Yasda.Api
```

Look at the log, between the build messages and the listening address:

```
fail: Microsoft.EntityFrameworkCore.Database.Command[20102]
      Failed executing DbCommand (6ms) ...
      SELECT migration_id, product_version FROM tickets."__EFMigrationsHistory" ORDER BY migration_id;
info: DatabaseStartup[0]
      Tickets schema is up to date
info: DatabaseStartup[0]
      Development data seeded
```

That red `fail:` line only appears on the very first run, and it's normal: EF Core asks the database which
migrations have run, and on a new database the history table doesn't exist yet. The two `info` lines are the
real result. Stop the app (Ctrl+C), then look inside with `psql`:

```bash
psql "host=localhost dbname=yasda user=yasda_dev password=yasda_dev_only" \
  -c "SELECT id, subject, status FROM tickets.ticket ORDER BY id"
```

```
 id |               subject                |  status
----+--------------------------------------+----------
  1 | Printer on the 3rd floor won't print | New
  2 | Can't connect to the VPN from home   | Pending
  3 | New mouse, please                    | Resolved
(3 rows)
```

The statuses are the ones the domain methods left behind: the VPN ticket was taken, answered and marked pending;
the mouse ticket was resolved. Run the app again, and the log has no `fail:` line: the migrations are already
applied. The "seeded" line still prints, but nothing is added, because there are tickets already. Run the `psql` query
again and you still see three.

## 7. Prove it: the database and the map

### One shared app and one database for the tests

Until now, every test class started its own copy of the app. With a database, that has a problem: two copies
migrating the same database at once. And we don't want the tests writing into the tickets you click through while
developing.

So the tests get a database of their own, `yasda_test`, and all test classes share one running app. The first is
a change to the factory from lesson 02. Before:

```csharp
public sealed class CustomWebAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");
}
```

After:

```csharp
// tests/Yasda.Integration.Tests/CustomWebAppFactory.cs
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Yasda.Integration.Tests;

// The whole app, started in memory inside the test process, on its own PostgreSQL database (yasda_test), so the tests
// never touch the data you click through in development.
public sealed class CustomWebAppFactory : WebApplicationFactory<Program>
{
    // The server these tests use. Set YASDA_TEST_DB to point at another one.
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("YASDA_TEST_DB")
        ?? "Host=localhost;Port=5432;Database=yasda_test;Username=yasda_dev;Password=yasda_dev_only";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:YasdaDb", ConnectionString);
    }

    // A connection for tests that look at the database itself. Starts the app first if no test has yet: the app is
    // what migrates the database.
    public NpgsqlConnection OpenDatabase()
    {
        _ = Server;
        var connection = new NpgsqlConnection(ConnectionString);
        connection.Open();
        return connection;
    }
}
```

`UseSetting` overrides one configuration value for the test app, so the same code runs against `yasda_test`.
`OpenDatabase` hands a test a plain connection for looking at the tables themselves. It touches `Server` first, which starts the app if no test has yet: the app is what migrates the database, and a database test that ran first would otherwise find no tables. Set `YASDA_TEST_DB` to a
connection string if your server or login differs.

This is a simpler factory than a real product wants, and we'll replace it with a throwaway database in a
container in lesson 12, where you'll see the problem it solves: this one reuses the same database between runs.

Next, one place that says "these classes share one app", a **collection**. xUnit's naming habit is a class ending
in `Collection`, which the analyzers read as "a list type" (CA1711). Switch that rule off in `tests/.editorconfig`, with
the reason beside it, as in lesson 02:

```ini
# CA1711: "...Collection" is xUnit's naming convention for a collection definition, not a list type.
dotnet_diagnostic.CA1711.severity = none
```

Then the collection itself:

```csharp
// tests/Yasda.Integration.Tests/IntegrationCollection.cs
namespace Yasda.Integration.Tests;

// Every integration test class shares one running app and one database. xUnit runs the classes of a collection one
// after another, so two tests never migrate or change the same tables at the same moment.
[CollectionDefinition("Integration")]
public class IntegrationCollection : ICollectionFixture<CustomWebAppFactory>;
```

xUnit runs the classes of one collection one after the other, and gives each the shared factory. Now the three
test classes you have change their first lines. Before, in `HostTests`, `PingTests` and `WiringTests`:

```csharp
public class PingTests(CustomWebAppFactory factory) : IClassFixture<CustomWebAppFactory>
```

After:

```csharp
[Collection("Integration")]
public class PingTests(CustomWebAppFactory factory)
```

Run `dotnet test`: all 56 still pass, now against one shared app.

### The database tests

```csharp
// tests/Yasda.Integration.Tests/DatabaseTests.cs
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Yasda.Modules.Tickets.Domain;
using Yasda.Modules.Tickets.Infrastructure;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Integration.Tests;

// What the migrations built, checked in the database itself, and that a ticket survives a trip to it and back.
[Collection("Integration")]
public class DatabaseTests(CustomWebAppFactory factory)
{
    private static readonly DateTime Now = new(2026, 11, 19, 9, 0, 0, DateTimeKind.Utc);

    // Every module that registered a database, so these tests grow with the app without being edited.
    // A module's schema is its name in lower case: Tickets -> tickets.
    private string[] Schemas
    {
        get
        {
            using var scope = factory.Services.CreateScope();
            var modules = scope.ServiceProvider.GetServices<IModuleDatabase>();
            return modules.Select(m => m.Module.ToLowerInvariant()).ToArray();
        }
    }

    // One column of text from a query, as a list.
    private async Task<List<string>> Column(string sql, params string[] schemas)
    {
        await using var connection = factory.OpenDatabase();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schemas", schemas);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
            values.Add(reader.GetString(0));
        return values;
    }

    [Fact]
    public async Task Every_module_has_its_own_schema_and_migration_history()
    {
        var histories = await Column(
            "SELECT table_schema FROM information_schema.tables WHERE table_name = '__EFMigrationsHistory'");

        histories.Should().BeEquivalentTo(Schemas);
    }

    [Fact]
    public async Task Nothing_of_ours_lands_in_the_public_schema()
    {
        var tables = await Column("SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'");

        tables.Should().BeEmpty();
    }

    [Fact]
    public async Task Tables_and_columns_are_snake_case()
    {
        var names = await Column(
            """
            SELECT table_name || '.' || column_name FROM information_schema.columns
            WHERE table_schema = ANY(@schemas) AND table_name <> '__EFMigrationsHistory'
              AND (table_name ~ '[A-Z]' OR column_name ~ '[A-Z]')
            """, Schemas);

        names.Should().BeEmpty("EFCore.NamingConventions maps every name to snake_case");
    }

    // A status the database doesn't know is refused by the database itself, whatever the code does.
    [Fact]
    public async Task The_database_refuses_a_status_it_does_not_know()
    {
        await using var connection = factory.OpenDatabase();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO tickets.ticket (subject, priority, status, requester_id, requester_name, created_utc, updated_utc)
            VALUES ('x', 'Normal', 'Banana', 1, 'Rita', now(), now())
            """, connection);

        var failure = await FluentActions.Invoking(() => command.ExecuteNonQueryAsync()).Should().ThrowAsync<PostgresException>();

        failure.Which.ConstraintName.Should().Be("ck_ticket_status");
    }

    [Fact]
    public async Task A_ticket_and_its_conversation_survive_a_round_trip()
    {
        long id;
        await using (var writeScope = factory.Services.CreateAsyncScope())
        {
            var db = writeScope.ServiceProvider.GetRequiredService<TicketsDbContext>();
            var ticket = Ticket.Create("Round trip", "First words", TicketPriority.High, new UserId(5), "Rita Cruz", Now);
            ticket.Take(new UserId(3), "Alan Reyes", Now);
            ticket.Reply(new UserId(3), "Alan Reyes", "A note for us", isInternal: true, authorIsStaff: true, Now);
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync();
            id = ticket.Id.Value;
        }

        // A new scope is a new DbContext: nothing is remembered, so this really reads the database.
        await using var readScope = factory.Services.CreateAsyncScope();
        var reader = readScope.ServiceProvider.GetRequiredService<TicketsDbContext>();
        var loaded = await reader.Tickets.FindAsync(new TicketId(id));

        loaded.Should().NotBeNull();
        loaded.Subject.Should().Be("Round trip");
        loaded.Priority.Should().Be(TicketPriority.High);
        loaded.Status.Should().Be(TicketStatus.Open);
        loaded.AssigneeId.Should().Be(new UserId(3));
        loaded.Messages.Should().HaveCount(2);
        loaded.Messages[1].IsInternal.Should().BeTrue();
    }

    [Fact]
    public async Task Seeding_twice_changes_nothing()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsDbContext>();
        var seeder = scope.ServiceProvider.GetRequiredService<IModuleDatabase>();
        var before = db.Tickets.Count();

        await seeder.SeedDevelopmentDataAsync(CancellationToken.None);

        db.Tickets.Count().Should().Be(before);
        before.Should().BeGreaterThanOrEqualTo(3, "the app seeded three sample tickets at startup");
    }
}
```

A few things to see:

- **`Schemas`** asks the app for every module that registered a database, and turns each name into its schema
  (`Tickets` becomes `tickets`). Today that's one. When Identity arrives, the tests check it too, unedited.
- **`Column`** runs one query and returns the first column as a list. Plain ADO.NET with Npgsql, no helper
  library yet. In lesson 08 Dapper makes this shorter.
- **`The_database_refuses_a_status_it_does_not_know`** inserts a row straight into the table with a made-up
  status, skipping the domain entirely. The database says no by itself, and names the constraint. That's what the
  check constraint is for.
- **`A_ticket_and_its_conversation_survive_a_round_trip`** writes in one scope and reads in another. A scope is
  a request's worth of services, so a new scope is a new `DbContext` with a fresh memory: the data has to come
  from the database, not from EF Core remembering what it just saved. It also proves the owned messages, the
  converters and the private fields all work.

Make one fail once. In `TicketsDatabase`, delete the line `if (await db.Tickets.AnyAsync(ct)) return;`, then run
`dotnet test tests/Yasda.Integration.Tests`:

```
failed Yasda.Integration.Tests.DatabaseTests.Seeding_twice_changes_nothing
  Expected db.Tickets.Count() to be 10, but found 13 (difference of 3).
```

Your numbers will differ, but the difference is 3: three more tickets each time. Put the line back.

### Edge cases covered in this lesson

| Test | The edge | Why it matters |
|---|---|---|
| `Every_module_has_its_own_schema_and_migration_history` | Where the history lives | Each module migrates on its own, from its own schema |
| `Nothing_of_ours_lands_in_the_public_schema` | A table in the default schema | Modules are islands, even in the database |
| `Tables_and_columns_are_snake_case` | A `RequesterId` column | Hand-written SQL in lesson 08 relies on the naming |
| `The_database_refuses_a_status_it_does_not_know` | Writing around the domain | The last line of defense is the database |
| `A_ticket_and_its_conversation_survive_a_round_trip` | A trip with nothing remembered | The mapping works in both directions |
| `Seeding_twice_changes_nothing` | A second startup | Seed data must be idempotent |

## Mistakes to avoid

- Running the tests against the database you click through. They write rows; give them their own (`yasda_test`).
- Editing a migration that has already run anywhere. Add a new one.
- Committing a generated migration unread.
- Forgetting `Microsoft.EntityFrameworkCore.Design` in the *host*: `dotnet ef` then stops with "Your startup
  project doesn't reference Microsoft.EntityFrameworkCore.Design".
- Turning `MigrateOnStartup` on in production, so a bad migration blocks a routine restart.
- Using the `postgres` administrator in the connection string. The app gets its own, smaller login.
- Putting a table in the default (`public`) schema because it was easier. The test above catches it.
- Using a real person's data in the seed. Make it up.

## Let's run it!

```bash
dotnet build
dotnet test
```

The build shows **0 warnings and 0 errors**, and the tests report 62 passed: the earlier 56, and six database
tests. Run the app once and look at the three tickets with `psql`, as in step 6. Then save the moment in Git:

```bash
git add .
git commit -m "Lesson 05: PostgreSQL, EF Core, the tickets schema, sample data"
```

## Let's check!

1. Why does each module get its own schema and its own migration history?
2. What is change tracking, and what does it cost?
3. Why do we store a ticket's status by name instead of by number?
4. A teammate edits `InitialCreate` to add a column. Why is that a problem, and what should they do instead?
5. What makes the seed data idempotent, and why does the app depend on that?
6. Why does `A_ticket_and_its_conversation_survive_a_round_trip` read in a second scope?
7. Why does the connection string for production come from the server's settings, not from `appsettings.json`?
8. What does the check constraint on `status` protect against that the domain doesn't?

And that's where this lesson ends. The Tickets module now remembers: a schema, a migration, sample tickets, and
tests that talk to a real PostgreSQL. Next time, we let people in: ASP.NET Core Identity, the five roles, and the
sign-in page, plus our first browser tests. Good luck, developer!

---

← [Lesson 04 — The domain model](04-domain-model.md) · Next: [Lesson 06 — Sign-in](06-sign-in.md) →
