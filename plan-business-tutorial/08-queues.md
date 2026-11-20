# Lesson 08 — Queues: Reading Tickets Fast, in the Address

Good day, developer! Until now, we've only written. Today we read. An agent opens the **queue**: a table of
tickets they can filter, sort and page through. Each ticket has a page of its own, with its whole conversation.
And all of it comes from a different tool than the one that saves: **Dapper**, calling **SQL functions** that
live in PostgreSQL. We'll also see why a list's search, sort and page belong in the address bar, and why
the server, never the browser, does the sorting. It takes about three hours. Are you ready? Let's begin!

## What we'll build

| Piece | What it is |
|---|---|
| **Dapper** | A tiny library that runs SQL and turns the rows into C# records |
| **Three SQL functions** | `search_tickets` and `count_tickets` for the queue, `get_ticket` and `get_ticket_messages` for one ticket, shipped by a migration |
| **`SearchTickets` and `GetTicket`** | The two read features, as queries: the first features that never touch EF Core |
| **The queue page** | Filters, server-side sorting and paging, all in the address |
| **The ticket page** | The details and the conversation, read-only for now |
| **Small shared pieces** | `PagedResult`, `SearchText`, an allow-list rule for sorting, and a handful of screen components |
| **Tests** | The visibility rules, filters and paging; sorting and its allow-list; the walls that keep reads and writes apart |

## Why we build it this way

### The pain: reading with the tool that writes

The queue is the screen agents stare at all day. Loading it with EF Core means building a full `Ticket` object, with
its messages, for every row on the page, just to show a subject and a status. EF Core tracks each of them in memory,
in case you change one. That is what it's for when you *save* a ticket. For *showing* twenty, it's waste.

And a list wants things a save never does: filter by this and that, search a word, sort by any column, count the total
for "Page 2 of 9". Written in C# over loaded objects, that's slow. Written as SQL pasted together from the request, it's
dangerous:

```csharp
var sql = "SELECT * FROM tickets.ticket ORDER BY " + request.Sort;   // never: ?sort=id;DROP TABLE tickets.ticket
```

That's a **SQL injection**: the person who controls the text controls your database.

### The fix: Dapper and SQL functions, and a list that lives in the address

Two ideas.

**First, reads and writes use different tools.** Writes go through EF Core and the domain, because that's where the
rules are. Reads go through **Dapper**: you write the SQL, it runs it and fills a record for each row. No
tracking, no objects you don't need. This split is **CQRS-lite**, which lesson 03 promised: a command changes
something, a query only reads, and now they even use different libraries. An architecture test will keep them apart.

**Second, the SQL lives in the database, as functions.** A PostgreSQL **function** is a named query stored in the
schema: `SELECT * FROM tickets.search_tickets(...)`. Our code passes parameters, never SQL text. It shows in one file
per function, reviewed like code, and shipped by a migration so every database has the same one. (They're functions,
not "stored procedures", because in PostgreSQL a procedure can't return rows; a function that `RETURNS TABLE` can.)

And the sorting: the SQL never builds an `ORDER BY` from the request. It picks from a fixed list with `CASE`, and the API
refuses any name not on its **allow-list** before it gets there. Two locks on one door.

### The address holds the list

A list's filters, its sort and its page live in the **address**: `/tickets?status=Open&sort=priority&desc=true&page=2`.
That has real benefits. Back and Forward work. A bookmark or a pasted link shows the same list. And the page is just
links: a sort heading is a link, "Next" is a link. No state to lose when the page reloads.

Now, let's be honest about the cost here: we keep the SQL and the C# in two places, and a new filter touches the
function, the query record, the validator and the page. That's the price of keeping each in the tool that's best at it.
It's four small edits, and every one is checked by a test.

## 1. Dapper, and a way to get connections

```bash
dotnet add src/Modules/Tickets/Yasda.Modules.Tickets package Dapper --version 2.1.79
dotnet add src/Modules/Identity/Yasda.Modules.Identity package Dapper --version 2.1.79
dotnet add src/Shared/Yasda.Shared.Infrastructure package Dapper --version 2.1.79
dotnet add src/Shared/Yasda.Shared.BuildingBlocks package FluentValidation --version 12.1.1
```

| Package | What it's for | License |
|---|---|---|
| `Dapper` | Runs SQL and maps rows to records | Apache-2.0 |
| `FluentValidation` (in BuildingBlocks) | The shared sort and paging rules below use it | Apache-2.0 |

A read needs a database connection, but a module must not know how connections are made (that's
`Yasda.Shared.Infrastructure`, which only the host sees). So the shared project gets an interface, and the
infrastructure the real thing. The interface goes into `Data.cs`, next to `IModuleDatabase`. Replace the file:

```csharp
// src/Shared/Yasda.Shared.BuildingBlocks/Abstractions/Data.cs
using System.Data.Common;
using System.Reflection;

namespace Yasda.Shared.BuildingBlocks;

// A new connection per Dapper read; the caller disposes it.
public interface IDbConnectionFactory
{
    DbConnection Create();
}

// What the host needs from a module's database, without seeing inside it: bring the schema up to date, and (in
// development) put a few sample rows in.
public interface IModuleDatabase
{
    string Module { get; }
    Task MigrateAsync(CancellationToken ct);
    Task SeedDevelopmentDataAsync(CancellationToken ct);
}

public static class EmbeddedSql
{
    // The text of a .sql file that was compiled into the module's assembly.
    public static string Read(Assembly assembly, string fileName)
    {
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase)) continue;
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        throw new InvalidOperationException($"Embedded SQL '{fileName}' not found in {assembly.GetName().Name}.");
    }
}
```

`EmbeddedSql` reads a `.sql` file that was compiled into a module's assembly. We'll use it in a moment.

```csharp
// src/Shared/Yasda.Shared.Infrastructure/Data/PostgresConnections.cs
using System.Data.Common;
using Npgsql;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Shared.Infrastructure;

// Every read opens a connection from the one shared pool (the NpgsqlDataSource) and gives it back when done.
public sealed class PostgresConnectionFactory(NpgsqlDataSource dataSource) : IDbConnectionFactory
{
    public DbConnection Create()
    {
        return dataSource.CreateConnection();
    }
}

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

(`PostgresDatabase` moved here from its own file. Delete `Data/PostgresDatabase.cs`.) An `NpgsqlDataSource` is
Npgsql's pool of open connections: creating a connection from it is cheap, and disposing one hands it back. Then
the shared registrations, replacing the file from lesson 07:

```csharp
// src/Shared/Yasda.Shared.Infrastructure/DependencyInjection.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Shared.Infrastructure;

public static class DependencyInjection
{
    // The platform services every module codes against.
    public static IServiceCollection AddSharedInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("YasdaDb");
        if (string.IsNullOrEmpty(connectionString))
            throw new InvalidOperationException("ConnectionStrings:YasdaDb is not configured.");

        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddSingleton<IDbConnectionFactory, PostgresConnectionFactory>();

        // PostgreSQL names columns in snake_case (requester_name); our DTOs use PascalCase (RequesterName).
        // This tells Dapper that the two are the same name, for every read query in every module.
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

        services.AddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton(ErrorCatalog.From(typeof(CommonErrors)));
        return services;
    }
}
```

- **`AddSharedInfrastructure` now takes the configuration**, to read the connection string once.
- **`MatchNamesWithUnderscores`** tells Dapper that the SQL's `requester_name` and our record's `RequesterName` are the
  same name, for every read in every module. Set it once, here, and no query needs aliases.

In `Program.cs`, the call gets the configuration. Before:

```csharp
builder.Services.AddSharedInfrastructure();
```

After:

```csharp
builder.Services.AddSharedInfrastructure(builder.Configuration);
```

## 2. The SQL functions

One file per function, in the module, named `schema.function.vN.sql`. The `v1` is a promise from lesson 05: **a
released script is never edited.** To change a function, you add a `.v2.sql` file and a new migration.

```sql
-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- Priorities sort by urgency, not alphabetically ("High" < "Low" < "Normal" < "Critical" would be nonsense).
CREATE OR REPLACE FUNCTION tickets.priority_rank(p_priority text)
RETURNS integer
LANGUAGE sql IMMUTABLE
AS $$
    SELECT CASE p_priority WHEN 'Low' THEN 0 WHEN 'Normal' THEN 1 WHEN 'High' THEN 2 WHEN 'Critical' THEN 3 END;
$$;
```

```sql
-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- The queues and "my tickets". p_requester_id limits a requester to their own; p_assignee_id / p_unassigned pick a
-- queue. The sort is one of the allow-listed names (SearchTicketsValidator), matched by CASE, never pasted in.
CREATE OR REPLACE FUNCTION tickets.search_tickets(
    p_requester_id bigint, p_status text, p_priority text, p_assignee_id bigint, p_unassigned boolean, p_search text,
    p_sort text, p_desc boolean, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, subject text, status text, priority text, requester_id bigint, requester_name text,
               assignee_id bigint, assignee_name text, created_utc timestamptz, updated_utc timestamptz)
LANGUAGE sql STABLE
AS $$
    SELECT t.id, t.subject::text, t.status::text, t.priority::text, t.requester_id, t.requester_name::text,
           t.assignee_id, t.assignee_name::text, t.created_utc, t.updated_utc
    FROM tickets.ticket t
    WHERE (p_requester_id IS NULL OR t.requester_id = p_requester_id)
      AND (p_status IS NULL OR t.status = p_status)
      AND (p_priority IS NULL OR t.priority = p_priority)
      AND (p_assignee_id IS NULL OR t.assignee_id = p_assignee_id)
      AND (NOT p_unassigned OR t.assignee_id IS NULL)
      AND (p_search IS NULL OR t.subject ILIKE '%' || p_search || '%' OR t.requester_name ILIKE '%' || p_search || '%'
           OR t.id::text = p_search)
    ORDER BY
        CASE WHEN p_sort = 'id' AND NOT p_desc THEN t.id END ASC,
        CASE WHEN p_sort = 'id' AND p_desc     THEN t.id END DESC,
        CASE WHEN p_sort = 'subject' AND NOT p_desc THEN t.subject END ASC,
        CASE WHEN p_sort = 'subject' AND p_desc     THEN t.subject END DESC,
        CASE WHEN p_sort = 'status' AND NOT p_desc THEN t.status END ASC,
        CASE WHEN p_sort = 'status' AND p_desc     THEN t.status END DESC,
        CASE WHEN p_sort = 'priority' AND NOT p_desc THEN tickets.priority_rank(t.priority) END ASC,
        CASE WHEN p_sort = 'priority' AND p_desc     THEN tickets.priority_rank(t.priority) END DESC,
        CASE WHEN p_sort = 'requesterName' AND NOT p_desc THEN t.requester_name END ASC,
        CASE WHEN p_sort = 'requesterName' AND p_desc     THEN t.requester_name END DESC,
        CASE WHEN p_sort = 'assigneeName' AND NOT p_desc THEN t.assignee_name END ASC,
        CASE WHEN p_sort = 'assigneeName' AND p_desc     THEN t.assignee_name END DESC,
        CASE WHEN p_sort = 'createdUtc' AND NOT p_desc THEN t.created_utc END ASC,
        CASE WHEN p_sort = 'createdUtc' AND p_desc     THEN t.created_utc END DESC,
        CASE WHEN p_sort = 'updatedUtc' AND NOT p_desc THEN t.updated_utc END ASC,
        t.updated_utc DESC, t.id DESC
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;

CREATE OR REPLACE FUNCTION tickets.count_tickets(
    p_requester_id bigint, p_status text, p_priority text, p_assignee_id bigint, p_unassigned boolean, p_search text)
RETURNS bigint
LANGUAGE sql STABLE
AS $$
    SELECT COUNT(*) FROM tickets.ticket t
    WHERE (p_requester_id IS NULL OR t.requester_id = p_requester_id)
      AND (p_status IS NULL OR t.status = p_status)
      AND (p_priority IS NULL OR t.priority = p_priority)
      AND (p_assignee_id IS NULL OR t.assignee_id = p_assignee_id)
      AND (NOT p_unassigned OR t.assignee_id IS NULL)
      AND (p_search IS NULL OR t.subject ILIKE '%' || p_search || '%' OR t.requester_name ILIKE '%' || p_search || '%'
           OR t.id::text = p_search);
$$;
```

This is the queue. Let's read it:

- **`RETURNS TABLE (...)`** declares the columns, so Dapper can map each row.
- **The `WHERE` has one line per filter**, and each is written `(p_x IS NULL OR column = p_x)`: a filter left out
  (null) matches everything. One function serves every combination.
- **`p_requester_id` is how a requester sees only their own tickets**: the handler passes their id, and the function
  filters on it. For staff it's null, which means "all". The rule sits in one place, and it's tested.
- **`ILIKE '%' || p_search || '%'`** is a case-insensitive "contains". A search for a number matches the ticket's id.
- **`ORDER BY CASE WHEN p_sort = 'subject' AND NOT p_desc THEN t.subject END ASC, ...`** is the whole sort. Each
  `CASE` is null unless it's the chosen column and direction, so only the chosen one orders anything. Nothing is pasted
  in as text. The last two lines are the default: newest first, and the id to break ties so paging never repeats a row.
- **`tickets.priority_rank`** sorts priorities by urgency (Low, Normal, High, Critical), because alphabetically
  "Critical" comes before "High" and "Low" before "Normal", which is nonsense.
- **`OFFSET ... LIMIT`** is the page.

The same filters count the total, for "Page 1 of 9". It's in the same file, under the search:

```sql
CREATE OR REPLACE FUNCTION tickets.count_tickets(
    p_requester_id bigint, p_status text, p_priority text, p_assignee_id bigint, p_unassigned boolean, p_search text)
RETURNS bigint
LANGUAGE sql STABLE
AS $$
    SELECT COUNT(*) FROM tickets.ticket t
    WHERE (p_requester_id IS NULL OR t.requester_id = p_requester_id)
      AND (p_status IS NULL OR t.status = p_status)
      AND (p_priority IS NULL OR t.priority = p_priority)
      AND (p_assignee_id IS NULL OR t.assignee_id = p_assignee_id)
      AND (NOT p_unassigned OR t.assignee_id IS NULL)
      AND (p_search IS NULL OR t.subject ILIKE '%' || p_search || '%' OR t.requester_name ILIKE '%' || p_search || '%'
           OR t.id::text = p_search);
$$;
```

And one ticket, with its conversation:

```sql
-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- One ticket, or no row when it doesn't exist or p_requester_id says the caller may only see their own.
CREATE OR REPLACE FUNCTION tickets.get_ticket(p_id bigint, p_requester_id bigint)
RETURNS TABLE (id bigint, subject text, status text, priority text, requester_id bigint, requester_name text,
               assignee_id bigint, assignee_name text, created_utc timestamptz, updated_utc timestamptz,
               first_response_utc timestamptz, resolved_utc timestamptz, closed_utc timestamptz, cancel_reason text)
LANGUAGE sql STABLE
AS $$
    SELECT t.id, t.subject::text, t.status::text, t.priority::text, t.requester_id, t.requester_name::text,
           t.assignee_id, t.assignee_name::text, t.created_utc, t.updated_utc, t.first_response_utc, t.resolved_utc,
           t.closed_utc, t.cancel_reason::text
    FROM tickets.ticket t
    WHERE t.id = p_id AND (p_requester_id IS NULL OR t.requester_id = p_requester_id);
$$;

-- The conversation, oldest first. Internal notes only when the caller is staff.
CREATE OR REPLACE FUNCTION tickets.get_ticket_messages(p_id bigint, p_include_internal boolean)
RETURNS TABLE (id bigint, author_id bigint, author_name text, body text, is_internal boolean, created_utc timestamptz)
LANGUAGE sql STABLE
AS $$
    SELECT m.id, m.author_id, m.author_name::text, m.body::text, m.is_internal, m.created_utc
    FROM tickets.ticket_message m
    WHERE m.ticket_id = p_id AND (p_include_internal OR NOT m.is_internal)
    ORDER BY m.created_utc, m.id;
$$;
```

`get_ticket_messages` shows internal notes only when `p_include_internal` is true, which the handler sets for staff.
Again, the visibility rule is in one place.

### Shipping them with a migration

The functions must exist in every database. They're **embedded** in the module's assembly, so a migration can read them.
In `Yasda.Modules.Tickets.csproj`:

```xml
<ItemGroup>
  <EmbeddedResource Include="Infrastructure\Sql\**\*.sql" />
</ItemGroup>
```

Ask EF Core for an empty migration, then fill it in:

```bash
dotnet ef migrations add AddReadFunctions \
  --project src/Modules/Tickets/Yasda.Modules.Tickets \
  --startup-project src/Yasda.Api \
  --output-dir Infrastructure/Migrations
```

EF Core finds nothing to change in the model, so `Up` and `Down` come out empty. We write them by hand. Replace the
generated file's content:

```csharp
using Microsoft.EntityFrameworkCore.Migrations;
using Yasda.Shared.BuildingBlocks;

#nullable disable

namespace Yasda.Modules.Tickets.Infrastructure.Migrations
{
    // The read-side SQL functions ship with the schema. Never point a released migration at a changed file.
    public partial class AddReadFunctions : Migration
    {
        private static string Sql(string file)
        {
            return EmbeddedSql.Read(typeof(AddReadFunctions).Assembly, file);
        }

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Sql("tickets.priority_rank.v1.sql"));
            migrationBuilder.Sql(Sql("tickets.search_tickets.v1.sql"));
            migrationBuilder.Sql(Sql("tickets.get_ticket.v1.sql"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tickets.get_ticket_messages(bigint, boolean);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tickets.get_ticket(bigint, bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tickets.count_tickets(bigint, text, text, bigint, boolean, text);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tickets.search_tickets(bigint, text, text, bigint, boolean, text, text, boolean, integer, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tickets.priority_rank(text);");
        }
    }
}
```

`Up` runs each function's file; `Down` drops them again. The comment is the rule: never point a released migration at a
changed file. If `search_tickets` needs a new column, you'll create `tickets.search_tickets.v2.sql` and a migration
called, say, `SearchTicketsByTeam`, and leave this one alone. In the lessons your migration history grows with each
change like that. (The finished reference build squashes the history into one `InitialCreate` and one
`AddReadFunctions`, because nobody has installed it yet.)

## 3. Shared pieces

Four small things every list will use, written once in the shared project:

```csharp
// src/Shared/Yasda.Shared.BuildingBlocks/Abstractions/PagedResult.cs
namespace Yasda.Shared.BuildingBlocks;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}
```

```csharp
// src/Shared/Yasda.Shared.BuildingBlocks/Abstractions/SearchText.cs
namespace Yasda.Shared.BuildingBlocks;

// For the SQL functions' ILIKE '%' || p_search || '%': trimmed, null when blank, and with LIKE's own wildcards escaped,
// so a search for "50%" finds "50%" instead of everything that contains "50".
public static class SearchText
{
    public static string? ForLike(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return text.Trim().Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
    }
}
```

`SearchText.ForLike` makes a person's search safe for `LIKE`: it trims, turns a blank into `null` (no filter), and
escapes the three characters `LIKE` treats specially. Without it, a search for `50%` would match every ticket containing
"50", because `%` means "anything" in `LIKE`.

```csharp
// src/Shared/Yasda.Shared.BuildingBlocks/Messaging/ValidationRules.cs
using FluentValidation;

namespace Yasda.Shared.BuildingBlocks;

// Shape rules that many requests share, written once.
public static class ValidationRules
{
    public static IRuleBuilderOptions<T, int> ValidPage<T>(this IRuleBuilder<T, int> rule)
    {
        return rule.GreaterThan(0);
    }

    public static IRuleBuilderOptions<T, int> ValidPageSize<T>(this IRuleBuilder<T, int> rule)
    {
        return rule.InclusiveBetween(1, 100);
    }

    // A list's sort column: one of the names its SQL function knows (the JSON names of its columns), or none.
    // The SQL never builds an ORDER BY from the text, so this list is the whole contract.
    public static IRuleBuilderOptions<T, string?> ValidSort<T>(this IRuleBuilder<T, string?> rule, params string[] columns)
    {
        return rule.Must(sort => sort is null || columns.Contains(sort, StringComparer.Ordinal))
            .WithMessage($"Sort by one of: {string.Join(", ", columns)}.");
    }
}
```

`ValidSort` is the allow-list. The names it accepts are the JSON names of the columns (`requesterName`), exactly, in
the case the JSON uses. And last, the one place that decides who is staff:

```csharp
// src/Modules/Tickets/Yasda.Modules.Tickets/Features/TicketRoles.cs
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Features;

// Who may do what with tickets, decided once for every endpoint. Anyone signed in may file a ticket and follow
// their own; these roles work on everyone's.
internal static class TicketRoles
{
    public static readonly string[] Handlers = [Roles.Agent, Roles.TeamLead, Roles.Admin];
    public static readonly string[] Leads = [Roles.TeamLead, Roles.Admin];

    public static bool IsHandler(string role)
    {
        return Handlers.Contains(role);
    }
}
```

(`Handlers` are the roles that work on everyone's tickets. `Leads` are the two that may also assign; lesson 09 uses it.)

## 4. The two read features

Same two files as always, except the handler takes `IDbConnectionFactory` instead of a `DbContext`.

```csharp
// src/Modules/Tickets/Yasda.Modules.Tickets/Features/SearchTickets/SearchTicketsHandler.cs
using Dapper;
using FluentValidation;
using Yasda.Modules.Tickets.Domain;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Features;

// Assigned: "me" (my queue), "unassigned" (nobody's yet), or empty for all. A requester only ever gets their own.
internal sealed record SearchTicketsQuery(string? Status = null, string? Priority = null, string? Assigned = null, string? Search = null,
    string? Sort = null, bool Desc = false, int Page = 1, int PageSize = 20);

internal sealed record TicketSummaryDto(long Id, string Subject, string Status, string Priority, long RequesterId, string RequesterName,
    long? AssigneeId, string? AssigneeName, DateTime CreatedUtc, DateTime UpdatedUtc);

internal sealed class SearchTicketsValidator : AbstractValidator<SearchTicketsQuery>
{
    public SearchTicketsValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
        RuleFor(x => x.Sort).ValidSort("id", "subject", "status", "priority", "requesterName", "assigneeName", "createdUtc", "updatedUtc");
        RuleFor(x => x.Status).IsEnumName(typeof(TicketStatus), caseSensitive: true).When(x => x.Status is not null);
        RuleFor(x => x.Priority).IsEnumName(typeof(TicketPriority), caseSensitive: true).When(x => x.Priority is not null);
        RuleFor(x => x.Assigned).Must(a => a is null or "me" or "unassigned").WithMessage("Assigned is \"me\", \"unassigned\" or empty.");
    }
}

internal sealed class SearchTicketsHandler(IDbConnectionFactory connections, ICurrentUser currentUser)
{
    public async Task<PagedResult<TicketSummaryDto>> HandleAsync(SearchTicketsQuery q, CancellationToken ct)
    {
        // A requester only ever sees their own tickets, whatever the query asks for.
        long? requesterId = null;
        if (!TicketRoles.IsHandler(currentUser.Role)) requesterId = currentUser.UserId;

        long? assigneeId = null;
        if (q.Assigned == "me") assigneeId = currentUser.UserId;
        var unassignedOnly = q.Assigned == "unassigned";

        var args = new
        {
            RequesterId = requesterId, q.Status, q.Priority, AssigneeId = assigneeId, Unassigned = unassignedOnly,
            Search = SearchText.ForLike(q.Search), q.Sort, q.Desc, q.Page, q.PageSize
        };

        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM tickets.search_tickets(@RequesterId, @Status, @Priority, @AssigneeId, @Unassigned, @Search, @Sort, @Desc, @Page, @PageSize);
            SELECT tickets.count_tickets(@RequesterId, @Status, @Priority, @AssigneeId, @Unassigned, @Search);
            """, args, cancellationToken: ct));

        var items = (await multi.ReadAsync<TicketSummaryDto>()).ToList();
        var total = (int)await multi.ReadSingleAsync<long>();
        return new PagedResult<TicketSummaryDto>(items, q.Page, q.PageSize, total);
    }
}
```

```csharp
// src/Modules/Tickets/Yasda.Modules.Tickets/Features/SearchTickets/SearchTicketsEndpoint.cs
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Tickets.Features;

internal static class SearchTicketsEndpoint
{
    // The query string binds straight onto the query record; its defaults make every parameter optional.
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/tickets", async ([AsParameters] SearchTicketsQuery query, SearchTicketsHandler handler, CancellationToken ct) =>
        {
            var page = await handler.HandleAsync(query, ct);
            return TypedResults.Ok(page);
        })
        .RequireAuthorization();
    }
}
```

- **`SearchTicketsQuery`** is a **query**: a record of the filters, every one optional. `[AsParameters]` binds the
  address's query string straight onto it, so `?status=Open&page=2` becomes the record.
- **The validator sits next to the query**, because the endpoint receives the query itself. Page and size have limits,
  the sort must be on the list, and the status and priority must be real names (`IsEnumName`, case-sensitive, so
  `status=open` is refused: the names are exact).
- **The handler first decides what the caller may see.** A requester's id goes into `RequesterId`, whatever the query
  says. Then it sends *two* statements in one trip, the page of rows and the total. `QueryMultipleAsync` reads them
  back in order.
- **Dapper maps a row to `TicketSummaryDto`** by name, thanks to `MatchNamesWithUnderscores`.

```csharp
// src/Modules/Tickets/Yasda.Modules.Tickets/Features/GetTicket/GetTicketHandler.cs
using Dapper;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Features;

internal sealed record GetTicketQuery(long TicketId);

internal sealed record TicketDetailDto(long Id, string Subject, string Status, string Priority, long RequesterId, string RequesterName,
    long? AssigneeId, string? AssigneeName, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? FirstResponseUtc,
    DateTime? ResolvedUtc, DateTime? ClosedUtc, string? CancelReason, IReadOnlyList<TicketMessageDto> Messages);

internal sealed record TicketMessageDto(long Id, long AuthorId, string AuthorName, string Body, bool IsInternal, DateTime CreatedUtc);

// One row of tickets.get_ticket, exactly as the SQL function returns it.
internal sealed record TicketRow(long Id, string Subject, string Status, string Priority, long RequesterId, string RequesterName,
    long? AssigneeId, string? AssigneeName, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? FirstResponseUtc,
    DateTime? ResolvedUtc, DateTime? ClosedUtc, string? CancelReason);

internal sealed class GetTicketHandler(IDbConnectionFactory connections, ICurrentUser currentUser)
{
    public async Task<TicketDetailDto?> HandleAsync(GetTicketQuery q, CancellationToken ct)
    {
        // Staff see every ticket and the internal notes; anyone else only their own tickets, without the notes.
        var isStaff = TicketRoles.IsHandler(currentUser.Role);
        long? requesterId = null;
        if (!isStaff) requesterId = currentUser.UserId;

        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM tickets.get_ticket(@TicketId, @RequesterId);
            SELECT * FROM tickets.get_ticket_messages(@TicketId, @IsStaff);
            """, new { q.TicketId, RequesterId = requesterId, IsStaff = isStaff }, cancellationToken: ct));

        var row = await multi.ReadSingleOrDefaultAsync<TicketRow>();
        if (row is null) return null;
        var messages = (await multi.ReadAsync<TicketMessageDto>()).ToList();

        return new TicketDetailDto(row.Id, row.Subject, row.Status, row.Priority, row.RequesterId, row.RequesterName,
            row.AssigneeId, row.AssigneeName, row.CreatedUtc, row.UpdatedUtc, row.FirstResponseUtc, row.ResolvedUtc,
            row.ClosedUtc, row.CancelReason, messages);
    }
}
```

```csharp
// src/Modules/Tickets/Yasda.Modules.Tickets/Features/GetTicket/GetTicketEndpoint.cs
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Tickets.Features;

internal static class GetTicketEndpoint
{
    // Someone else's ticket is a 404, not a 403: a requester can't even learn that it exists.
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/tickets/{id:long}", async Task<Results<Ok<TicketDetailDto>, NotFound>> (long id, GetTicketHandler handler, CancellationToken ct) =>
        {
            var ticket = await handler.HandleAsync(new GetTicketQuery(id), ct);
            if (ticket is null) return TypedResults.NotFound();
            return TypedResults.Ok(ticket);
        })
        .RequireAuthorization();
    }
}
```

`GetTicketHandler` returns `null` for a ticket that doesn't exist **and** for a ticket a requester may not see. The
endpoint answers both `404 Not Found`. That's deliberate: a `403` would tell a stranger that ticket #7 exists. As the module
rules say, "does the thing in the URL exist?" is the endpoint's answer, with no exception.

Register the new handlers and endpoints. Your `TicketsModule.cs` is now:

```csharp
// src/Modules/Tickets/Yasda.Modules.Tickets/TicketsModule.cs
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Routing;
using Yasda.Modules.Tickets.Domain;
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
        services.AddScoped<CreateTicketHandler>();
        services.AddScoped<SearchTicketsHandler>();
        services.AddScoped<GetTicketHandler>();

        // Without includeInternalTypes no validator is found, and validation silently stops.
        services.AddValidatorsFromAssembly(typeof(TicketsModule).Assembly, includeInternalTypes: true);
        services.AddSingleton(ErrorCatalog.From(typeof(TicketsErrors)));
        return services;
    }

    public static IEndpointRouteBuilder MapTicketsEndpoints(this IEndpointRouteBuilder api)
    {
        var tickets = api.MapGroup("").WithTags("Tickets");
        CreateTicketEndpoint.Map(tickets);
        SearchTicketsEndpoint.Map(tickets);
        GetTicketEndpoint.Map(tickets);
        return api;
    }
}
```

Lesson 07 promised a faster implementation behind the contract, and the contract's callers won't notice. `UserDirectory`
reads with Dapper now, instead of asking `UserManager`:

```csharp
// src/Modules/Identity/Yasda.Modules.Identity/Infrastructure/UserDirectory.cs
using Dapper;
using Yasda.Modules.Identity.Contracts;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Identity.Infrastructure;

// Reads, so Dapper: one small query each, with no change tracking.
internal sealed class UserDirectory(IDbConnectionFactory connections) : IUserDirectory
{
    public async Task<long?> IdOfAsync(string email, CancellationToken ct)
    {
        await using var conn = connections.Create();
        return await conn.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
            "SELECT id FROM identity.users WHERE normalized_email = upper(@email)", new { email }, cancellationToken: ct));
    }

    public async Task<string?> NameOfAsync(long userId, CancellationToken ct)
    {
        await using var conn = connections.Create();
        return await conn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT full_name FROM identity.users WHERE id = @userId", new { userId }, cancellationToken: ct));
    }
}
```

Try the queue from a terminal, as the agent (use your token from lesson 07):

```bash
curl -s "http://localhost:5264/api/tickets?status=New&sort=priority&desc=true&pageSize=2" -H "Authorization: Bearer $TOKEN"
# {"items":[{"id":1,"subject":"Printer on the 3rd floor won't print","status":"New","priority":"High",...}],
#  "page":1,"pageSize":2,"totalCount":1,"totalPages":1}
```

## 5. The screens

### The pieces the list pages share

A page full of tickets needs a few small things. Each is a component or a helper, introduced where it's first used:

```csharp
// src/Yasda.Web/Shared/Format.cs
using System.Globalization;

namespace Yasda.Web.Shared;

public static class Format
{
    private static readonly CultureInfo Ph = CultureInfo.GetCultureInfo("en-PH");

    public static string Date(DateTime? utc)
    {
        if (utc is null) return "";
        return utc.Value.ToLocalTime().ToString("MMM d, yyyy h:mm tt", Ph);
    }

    // The machine-readable form a <time datetime="..."> wants: 2026-11-20T09:30:00Z.
    public static string Iso(DateTime utc)
    {
        return utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    // "Just now", "12m", "3h", "Yesterday", "Nov 3", "Nov 3, 2025": how long ago, the way people say it.
    public static string Ago(DateTime utc, DateTime nowUtc)
    {
        var since = nowUtc - utc;
        if (since < TimeSpan.FromMinutes(1)) return "Just now";   // also a clock a little ahead of ours
        if (since < TimeSpan.FromHours(1)) return $"{(int)since.TotalMinutes}m";

        var then = utc.ToLocalTime();
        var today = nowUtc.ToLocalTime().Date;
        if (then.Date == today) return $"{(int)since.TotalHours}h";
        if (then.Date == today.AddDays(-1)) return "Yesterday";
        if (then.Year == today.Year) return then.ToString("MMM d", Ph);
        return then.ToString("MMM d, yyyy", Ph);
    }
}
```

`Format.Ago` says "12m", "3h", "Yesterday", "Nov 3": how long ago, the way people say it. `Iso` is the form a `<time>`
element wants.

```razor
@* src/Yasda.Web/Shared/TimeAgo.razor *@
@inject TimeProvider Time

@* A time as people say it ("12m", "3h", "Yesterday"), with the exact time on hover and in the markup. *@
@if (Utc is null)
{
    @Empty
}
else
{
    <time datetime="@Format.Iso(Utc.Value)" title="@Format.Date(Utc)">@Format.Ago(Utc.Value, Time.GetUtcNow().UtcDateTime)</time>
}

@code {
    [Parameter, EditorRequired] public DateTime? Utc { get; set; }
    [Parameter] public string Empty { get; set; } = "Never";
}
```

`<time datetime="..." title="...">` puts the exact time in the markup and in a tooltip, with the friendly words on screen.

```razor
@* src/Yasda.Web/Shared/PriorityText.razor *@
@* A ticket's priority, styled as DESIGN.md §5.2 says. The word always carries the meaning; "Critical" is the only
   one in color. *@
@switch (Value)
{
    case "Critical":
        <strong class="critical">@Value</strong>
        break;
    case "High":
        <strong>@Value</strong>
        break;
    case "Low":
        <span class="muted">@Value</span>
        break;
    default:
        @Value
        break;
}

@code {
    [Parameter, EditorRequired] public string Value { get; set; } = "";
}
```

A priority is a word, never a colored badge; the word always carries the meaning. Only **Critical** is colored (and bold),
because it's the only one that must never be overlooked. High is bold. Low is muted.

```razor
@* src/Yasda.Web/Shared/SortHeader.razor *@
@inject NavigationManager Nav

@* A column heading that sorts the list on the server: a link to the same list with ?sort= set. The first click sorts
   ascending, the next descending. The API checks the name against its own list (ValidSort). *@
<th scope="col" class="@Class" aria-sort="@AriaSort">
    <a href="@Href">@Title</a>@Arrow
</th>

@code {
    [Parameter, EditorRequired] public string Column { get; set; } = "";
    [Parameter, EditorRequired] public string Title { get; set; } = "";
    [Parameter] public string? Sort { get; set; }
    [Parameter] public bool Desc { get; set; }
    [Parameter] public string? Class { get; set; }

    private bool Active => string.Equals(Sort, Column, StringComparison.Ordinal);
    private string Arrow
    {
        get
        {
            if (!Active) return "";
            if (Desc) return " ▼";
            return " ▲";
        }
    }

    private string AriaSort
    {
        get
        {
            if (!Active) return "none";
            if (Desc) return "descending";
            return "ascending";
        }
    }

    private string Href => Nav.GetUriWithQueryParameters(new Dictionary<string, object?>
    {
        ["sort"] = Column,
        ["desc"] = Active && !Desc ? "true" : null,
        ["page"] = null,
    });
}
```

`SortHeader` is a column heading that is a *link*: to the same address with `sort=` set. The first click sorts ascending,
the next descending. `aria-sort` tells a screen reader which column is sorted and how, and the arrow is for everyone else.
Nothing here sorts anything: the server does.

```razor
@* src/Yasda.Web/Shared/Pager.razor *@
@inject NavigationManager Nav

@* Previous and next as links (?page=), so every page of a list has its own address. *@
@if (Total > 0)
{
    <nav aria-label="Pages" class="actions">
        @if (Page > 1)
        {
            <a href="@HrefFor(Page - 1)">Previous</a>
        }
        <span>Page @Page of @Pages (@Total @Noun)</span>
        @if (Page < Pages)
        {
            <a href="@HrefFor(Page + 1)">Next</a>
        }
    </nav>
}

@code {
    [Parameter, EditorRequired] public int Page { get; set; }
    [Parameter, EditorRequired] public int PageSize { get; set; }
    [Parameter, EditorRequired] public int Total { get; set; }
    [Parameter] public string Noun { get; set; } = "rows";

    private int Pages => Math.Max(1, (Total + PageSize - 1) / PageSize);

    private string HrefFor(int page) =>
        Nav.GetUriWithQueryParameter("page", page > 1 ? page : null);
}
```

`Pager` is "Previous · Page 2 of 9 · Next", as links with `?page=`.

```csharp
// src/Yasda.Web/Shared/Url.cs
namespace Yasda.Web.Shared;

public static class Url
{
    // api/tickets?status=Open&search=printer, leaving out anything empty: the API would reject "status=".
    public static string With(string path, params (string Name, object? Value)[] query)
    {
        var parts = new List<string>();
        foreach (var (name, value) in query)
        {
            var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(text)) continue;
            parts.Add($"{name}={Uri.EscapeDataString(text)}");
        }
        if (parts.Count == 0) return path;
        return $"{path}?{string.Join("&", parts)}";
    }
}
```

`Url.With` builds the API address from the page's own values, leaving out anything empty, because the API refuses
`status=` with nothing after it.

```csharp
// src/Yasda.Web/Shared/ListMemory.cs
namespace Yasda.Web.Shared;

// The last address of each list in this browser tab, for the trail on a detail page: "Users" goes back to the list
// as you left it (its search, sort and page), not to an empty one.
public sealed class ListMemory
{
    private readonly Dictionary<string, string> _last = new(StringComparer.OrdinalIgnoreCase);

    public void Remember(string relativeUri) => _last[relativeUri.Split('?')[0]] = relativeUri;

    public string Last(string path) => _last.GetValueOrDefault(path, path);
}
```

A ticket page has a trail back to the queue ("Tickets › #7 Printer…"). `ListMemory` remembers the queue's last address,
with its filters, so "Tickets" goes back to the list **as you left it**, not to an empty one.

The top of every page grows a trail, a place for actions, and a wide variant for lists. Replace `PageHeader.razor`
from lesson 07:

```razor
@* src/Yasda.Web/Shared/PageHeader.razor *@
@* The top of every page: where it sits (a trail of links, for a page under a list), its title with the status line
   beside it, a line of facts or a sentence on what the page is for, and what can be done here. *@
@if (Trail is not null)
{
    <nav aria-label="Breadcrumb" class="trail">
        @foreach (var (text, href) in Trail)
        {
            <a href="@href">@text</a><text> › </text>
        }
        <span aria-current="page">@Title</span>
    </nav>
}
<div class="@(List ? "page-head list" : "page-head")">
    <h1>@Title</h1>
    <NoticeArea />
</div>
@if (Description is not null)
{
    <p>@Description</p>
}
@if (Actions is not null)
{
    <div class="actions">@Actions</div>
}

@code {
    [Parameter, EditorRequired] public string Title { get; set; } = "";
    [Parameter] public IReadOnlyList<(string Text, string Href)>? Trail { get; set; }
    [Parameter] public RenderFragment? Description { get; set; }
    [Parameter] public RenderFragment? Actions { get; set; }
    // A List page (DESIGN.md §4.3) gets the wide column.
    [Parameter] public bool List { get; set; }
}
```

### The models, `ApiClient.Get`, and the setup

The browser app's copies of the API's shapes, and the choices the queue's filters offer:

```csharp
// src/Yasda.Web/Models/Models.cs
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Web.Models;

// The API's JSON shapes, as the browser sees them. The server's own types are internal to each module, so the
// browser app keeps its own copies: the HTTP contract is the only thing the two share.

public sealed record TicketSummary(long Id, string Subject, string Status, string Priority, long RequesterId, string RequesterName,
    long? AssigneeId, string? AssigneeName, DateTime CreatedUtc, DateTime UpdatedUtc);

public sealed record TicketMessage(long Id, long AuthorId, string AuthorName, string Body, bool IsInternal, DateTime CreatedUtc);

public sealed record TicketDetail(long Id, string Subject, string Status, string Priority, long RequesterId, string RequesterName,
    long? AssigneeId, string? AssigneeName, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? FirstResponseUtc,
    DateTime? ResolvedUtc, DateTime? ClosedUtc, string? CancelReason, List<TicketMessage> Messages);

// The choices the API accepts, in the order people think of them.
public static class TicketChoices
{
    public static readonly string[] Priorities = ["Low", "Normal", "High", "Critical"];
    public static readonly string[] Statuses = ["New", "Open", "Pending", "Resolved", "Closed", "Cancelled"];

    // Who works on tickets. The API decides; the screens only hide what someone can't do anyway.
    public static bool IsStaff(string? role)
    {
        return role is Roles.Agent or Roles.TeamLead or Roles.Admin;
    }
}
```

`TicketChoices.IsStaff` uses the role names from BuildingBlocks. The browser app may not reference that project (it
references no module and no shared code), so the one file with the role names is **compiled into** it, as a link, not
copied. In `Yasda.Web.csproj` add:

```xml
<!-- Role names are defined once, in BuildingBlocks. The file is compiled in here too, not copied. -->
<ItemGroup>
  <Compile Include="..\Shared\Yasda.Shared.BuildingBlocks\Abstractions\Roles.cs" Link="Shared\Roles.cs" />
</ItemGroup>
```

`ApiClient` learns to read. Add this method above `Create`, and this record above the class:

```csharp
public sealed record PagedResult<T>(List<T> Items, int Page, int PageSize, int TotalCount);
```

```csharp
public async Task<T?> Get<T>(string url)
{
    var response = await http.GetAsync(url);
    if (!response.IsSuccessStatusCode)
    {
        await Fail(response);
        return default;
    }
    return await response.Content.ReadFromJsonAsync<T>();
}
```

and `CookieAuthenticationStateProvider` shows who is signed in, for the navigation and the queue to adapt. Add one
property under `_loaded`:

```csharp
public CurrentUser? User => _user;
```

Register the two new services in `Yasda.Web/Program.cs`, and add `using Yasda.Web.Shared;`:

```csharp
builder.Services.AddScoped<ListMemory>();
builder.Services.AddSingleton(TimeProvider.System);
```

### The queue

```razor
@* src/Yasda.Web/Pages/Tickets/Tickets.razor *@
@page "/tickets"
@inject ApiClient Api
@inject NavigationManager Nav
@inject ListMemory Memory
@inject CookieAuthenticationStateProvider Auth

<PageTitle>@Name · YASDA</PageTitle>

<PageHeader Title="@(_result is null ? Name : $"{Name} ({_result.TotalCount})")" List="true">
    <Actions><a href="tickets/new">New ticket</a></Actions>
</PageHeader>

@* The list's filters, sort and page live in the address, so Back, a bookmark or a pasted link shows the same list. *@
<EditForm Model="this" OnSubmit="Find" class="search" role="search">
    <div class="field">
        <label for="search">Subject, requester or number</label>
        <InputText id="search" type="search" @bind-Value="_search" />
    </div>
    @if (IsStaff)
    {
        <div class="field">
            <label for="assigned">Assigned to</label>
            <InputSelect id="assigned" @bind-Value="_assigned">
                <option value="">Anyone</option>
                <option value="me">Me</option>
                <option value="unassigned">Nobody yet</option>
            </InputSelect>
        </div>
    }
    <div class="field">
        <label for="status">Status</label>
        <InputSelect id="status" @bind-Value="_status">
            <option value="">Any</option>
            @foreach (var s in TicketChoices.Statuses)
            {
                <option value="@s">@s</option>
            }
        </InputSelect>
    </div>
    <div class="field">
        <label for="priority">Priority</label>
        <InputSelect id="priority" @bind-Value="_priority">
            <option value="">Any</option>
            @foreach (var p in TicketChoices.Priorities)
            {
                <option value="@p">@p</option>
            }
        </InputSelect>
    </div>
    <button type="submit">Apply</button>
    <a href="tickets">Clear</a>
</EditForm>

@if (Showing.Count > 0)
{
    <p>Showing: @string.Join(", ", Showing). <a href="tickets">Clear filters</a></p>
}

@if (_result is null)
{
    <p role="status">Loading…</p>
}
else if (_result.Items.Count == 0)
{
    <p>No tickets match. <a href="tickets">Clear filters</a> or <a href="tickets/new">file a new ticket</a>.</p>
}
else
{
    <TableBox Label="Tickets">
        <table>
            <thead>
                <tr>
                    <SortHeader Column="id" Title="#" Sort="@Sort" Desc="Desc" Class="num" />
                    <SortHeader Column="subject" Title="Subject" Sort="@Sort" Desc="Desc" />
                    <SortHeader Column="status" Title="Status" Sort="@Sort" Desc="Desc" />
                    <SortHeader Column="priority" Title="Priority" Sort="@Sort" Desc="Desc" />
                    @if (IsStaff)
                    {
                        <SortHeader Column="requesterName" Title="Requester" Sort="@Sort" Desc="Desc" />
                    }
                    <SortHeader Column="assigneeName" Title="Assigned to" Sort="@Sort" Desc="Desc" />
                    <SortHeader Column="updatedUtc" Title="Updated" Sort="@Sort" Desc="Desc" />
                </tr>
            </thead>
            <tbody>
                @foreach (var t in _result.Items)
                {
                    <tr>
                        <td class="num">@t.Id</td>
                        <td class="truncate"><a href="@($"tickets/{t.Id}")">@t.Subject</a></td>
                        <td>@t.Status</td>
                        <td><PriorityText Value="@t.Priority" /></td>
                        @if (IsStaff)
                        {
                            <td>@t.RequesterName</td>
                        }
                        <td>@(t.AssigneeName ?? "Nobody yet")</td>
                        <td><TimeAgo Utc="t.UpdatedUtc" /></td>
                    </tr>
                }
            </tbody>
        </table>
    </TableBox>
    <Pager Page="_result.Page" PageSize="_result.PageSize" Total="_result.TotalCount" Noun="tickets" />
}

@code {
    private const int PageSize = 20;

    [SupplyParameterFromQuery] public string? Search { get; set; }
    [SupplyParameterFromQuery] public string? Assigned { get; set; }
    [SupplyParameterFromQuery] public string? Status { get; set; }
    [SupplyParameterFromQuery] public string? Priority { get; set; }
    [SupplyParameterFromQuery] public string? Sort { get; set; }
    [SupplyParameterFromQuery] public bool Desc { get; set; }
    [SupplyParameterFromQuery(Name = "page")] public int? PageNumber { get; set; }

    private PagedResult<TicketSummary>? _result;
    private string? _search, _assigned, _status, _priority;

    private bool IsStaff => TicketChoices.IsStaff(Auth.User?.Role);
    private string Name => IsStaff ? "Tickets" : "My tickets";

    // The filters in force, in words: "Showing: Open, High, assigned to me."
    private List<string> Showing
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(Status)) parts.Add(Status);
            if (!string.IsNullOrEmpty(Priority)) parts.Add(Priority);
            if (Assigned == "me") parts.Add("assigned to me");
            if (Assigned == "unassigned") parts.Add("assigned to nobody yet");
            if (!string.IsNullOrWhiteSpace(Search)) parts.Add($"matching \"{Search}\"");
            return parts;
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        await Auth.GetAuthenticationStateAsync();
        _search = Search;
        _assigned = Assigned ?? "";
        _status = Status ?? "";
        _priority = Priority ?? "";
        Memory.Remember(Nav.ToBaseRelativePath(Nav.Uri));
        _result = await Api.Get<PagedResult<TicketSummary>>(Url.With("api/tickets", ("search", Search), ("assigned", Assigned),
            ("status", Status), ("priority", Priority), ("sort", Sort), ("desc", Desc ? true : null), ("page", PageNumber),
            ("pageSize", PageSize)));
    }

    // A new search starts again from page 1: page 3 of the old results may not exist.
    private void Find() => Nav.NavigateTo(Nav.GetUriWithQueryParameters(new Dictionary<string, object?>
    {
        ["search"] = string.IsNullOrWhiteSpace(_search) ? null : _search.Trim(),
        ["assigned"] = string.IsNullOrEmpty(_assigned) ? null : _assigned,
        ["status"] = string.IsNullOrEmpty(_status) ? null : _status,
        ["priority"] = string.IsNullOrEmpty(_priority) ? null : _priority,
        ["page"] = null,
    }));
}
```

This is the biggest page so far, so let's walk it:

- **`[SupplyParameterFromQuery]`** fills each property from the address: `?status=Open` sets `Status`. The page never
  keeps the list's state anywhere else.
- **`OnParametersSetAsync`** runs whenever the address changes (a filter, a sort, a page), asks the API with the same
  values, and shows the result. A click on a link *is* a change of address, so there is no event code at all.
- **The search form** is a plain `EditForm`. **Apply** doesn't call the API: it builds the new address
  (`GetUriWithQueryParameters`) and navigates to it, with `page` cleared, because page 3 of the old results may not exist
  for the new ones.
- **A requester sees "My tickets"**, with no requester column and no "Assigned to" filter. The API shows them only theirs
  anyway; the page just doesn't offer what doesn't apply.
- **The empty list says what to do next** ("No tickets match. Clear filters or file a new ticket."), not an empty table.
- **`Showing: Open, High, assigned to me.`** says in words what the filters are, with a link to clear them.
- **`TableBox`** (lesson 07) lets the table scroll sideways inside its own box on a phone, so the page itself never does.

### The ticket's page

```razor
@* src/Yasda.Web/Pages/Tickets/TicketPage.razor *@
@page "/tickets/{Id:long}"
@inject ApiClient Api
@inject ListMemory Memory

<PageTitle>@(_ticket is null ? "Ticket" : $"#{_ticket.Id} {_ticket.Subject}") · YASDA</PageTitle>

@if (_ticket is null)
{
    <PageHeader Title="Ticket" Trail="@([("Tickets", Memory.Last("tickets"))])" />
    <p role="status">Loading…</p>
}
else
{
    <PageHeader Title="@($"#{_ticket.Id} {_ticket.Subject}")" Trail="@([("Tickets", Memory.Last("tickets"))])">
        <Description>
            Status: @_ticket.Status · Priority: <PriorityText Value="@_ticket.Priority" /> ·
            Assigned: @(_ticket.AssigneeName ?? "Nobody yet") · Requester: @_ticket.RequesterName ·
            Filed <TimeAgo Utc="_ticket.CreatedUtc" /> · Updated <TimeAgo Utc="_ticket.UpdatedUtc" />
        </Description>
    </PageHeader>

    @if (_ticket.CancelReason is not null)
    {
        <p>Cancelled because: @_ticket.CancelReason</p>
    }

    <section class="thread" aria-labelledby="conversation">
        <h2 id="conversation">Conversation</h2>
        @foreach (var message in _ticket.Messages)
        {
            <article class="message">
                <h3>@message.AuthorName, <TimeAgo Utc="message.CreatedUtc" /></h3>
                @if (message.IsInternal)
                {
                    <p><strong>Internal note:</strong> only staff see this.</p>
                }
                <p class="message-body">@message.Body</p>
            </article>
        }
    </section>
}

@code {
    [Parameter] public long Id { get; set; }

    private TicketDetail? _ticket;

    protected override async Task OnParametersSetAsync()
    {
        _ticket = await Api.Get<TicketDetail>($"api/tickets/{Id}");
    }
}
```

Today's page is read-only: the details in a line, and the conversation as a list of `<article>`s, each with its author,
when, and a note when it's internal. The actions arrive in lessons 09 to 11. `Memory.Last("tickets")` is the trail back.

Filing a ticket now lands on it. In `NewTicket.razor`, add `@inject NavigationManager Nav` under the first inject, and
replace the end of `Send`. Before:

```csharp
        // Sent: the form is empty again, ready for the next one.
        _subject = "";
        _message = "";
        _priority = "Normal";
```

After:

```csharp
        // Sent: nothing is left to lose on the way to the ticket's page.
        _subject = "";
        _message = "";
        Nav.NavigateTo($"tickets/{id}");
```

The status line follows the person to the new page ("Ticket sent. We'll answer here."), because the message survives a
navigation that the code itself makes. And the navigation, with a link to the queue, named for the person's role:

```razor
@* src/Yasda.Web/Layout/NavMenu.razor *@
@inject CookieAuthenticationStateProvider Auth
@inject NavigationManager Nav

@* Each person sees the parts of the system their role uses. The API enforces the same rules; hiding a link is only
   a convenience. *@
<nav aria-label="Main">
    <ul class="links">
        <li><NavItem Href="">Home</NavItem></li>
        <li><NavItem Href="tickets">@(TicketChoices.IsStaff(Auth.User?.Role) ? "Tickets" : "My tickets")</NavItem></li>
        <li><NavItem Href="tickets/new">New ticket</NavItem></li>
        <li><NavItem Href="errors">Error codes</NavItem></li>
        <li><button type="button" class="link" @onclick="SignOut">Sign out</button></li>
    </ul>
</nav>

@code {
    private async Task SignOut()
    {
        await Auth.SignOutAsync();
        Nav.NavigateTo("login");
    }
}
```

Add the styles the new pages use at the end of `app.css`, and the wide column's size to the `:root` block:

```css
    --max-width-wide: 1200px;
```

```css
.trail {
    margin: 0 0 var(--gap-s);
}

/* A list page gets the wide column. */
.page:has(.page-head.list) {
    max-width: var(--max-width-wide);
}

th.num, td.num {
    text-align: right;
}

/* The subject takes the room that's left and ends in "...": the link still holds the whole text. */
td.truncate {
    max-width: 0;
    width: 100%;
    min-width: 20ch;
    overflow: hidden;
    white-space: nowrap;
    text-overflow: ellipsis;
}

/* Priority: the word always says it; color only for the most urgent. */
.critical {
    color: var(--error);
    font-weight: bold;
}

input:disabled, select:disabled, textarea:disabled {
    color: var(--text-muted);
    border-style: dashed;
}

/* A ticket's conversation: each message under its heading, separated by a rule, its text kept as typed. */
.thread {
    max-width: var(--measure);
}

.message {
    border-top: var(--border);
    padding-top: var(--gap-s);
    margin-bottom: var(--gap-m);
}

.message h3 {
    margin: 0;
}

.message-body {
    white-space: pre-wrap;
    margin: var(--gap-s) 0 0;
}
```

Run the app and sign in as `agent@yasda.local`. Open **Tickets**. Click **Priority** twice. Look at the address. Press
**Back**. Filter by "Nobody yet". Open a ticket, then click **Tickets** in the trail: you're back at the filtered queue.

## 6. Prove it: who sees what, and what the list refuses

### The queue and the ticket page

Add these tests to `TicketTests` (inside the class, after the ones from lesson 07). They file tickets with fresh
`Guid` words in their subjects, so they find *their* tickets among whatever other tests left in the database:

```csharp
    // ---- Reading tickets back: the queue and the ticket page ----

    private sealed record QueueRow(long Id, string Subject, string Status, string Priority, string RequesterName, long? AssigneeId);
    private sealed record QueuePage(List<QueueRow> Items, int Page, int PageSize, int TotalCount);
    private sealed record TicketPageView(long Id, string Subject, string Status, string Priority, List<MessageView> Messages);
    private sealed record MessageView(string AuthorName, string Body, bool IsInternal);

    private static async Task<long> FileOne(HttpClient client, string subject, string priority = "Normal")
    {
        var response = await client.PostAsJsonAsync("/api/tickets", new { subject, message = "Details for " + subject, priority });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CreatedId>())!.Id;
    }

    [Fact]
    public async Task The_ticket_page_shows_the_ticket_and_its_first_message()
    {
        var requester = await factory.ClientFor("requester@yasda.local");
        var id = await FileOne(requester, $"Scanner {Guid.NewGuid():N}", "High");

        var ticket = await requester.GetFromJsonAsync<TicketPageView>($"/api/tickets/{id}");

        ticket!.Status.Should().Be("New");
        ticket.Priority.Should().Be("High");
        ticket.Messages.Should().ContainSingle().Which.Body.Should().StartWith("Details for Scanner");
    }

    [Fact]
    public async Task A_ticket_that_does_not_exist_is_404()
    {
        var agent = await factory.ClientFor("agent@yasda.local");

        var response = await agent.GetAsync("/api/tickets/999999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Staff_see_everyones_tickets_and_a_requester_only_their_own()
    {
        var someoneElse = await factory.ClientFor(await factory.CreateUserAsync(Roles.Requester));
        var theirs = await FileOne(someoneElse, $"Theirs {Guid.NewGuid():N}");
        var requester = await factory.ClientFor("requester@yasda.local");
        var mine = await FileOne(requester, $"Mine {Guid.NewGuid():N}");
        var agent = await factory.ClientFor("agent@yasda.local");

        var staffQueue = await agent.GetFromJsonAsync<QueuePage>("/api/tickets?pageSize=100");
        var myQueue = await requester.GetFromJsonAsync<QueuePage>("/api/tickets?pageSize=100");

        staffQueue!.Items.Select(t => t.Id).Should().Contain([mine, theirs]);
        myQueue!.Items.Select(t => t.Id).Should().Contain(mine).And.NotContain(theirs);
        (await requester.GetAsync($"/api/tickets/{theirs}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_queue_filters_by_priority_and_by_search_text()
    {
        var requester = await factory.ClientFor("requester@yasda.local");
        var word = Guid.NewGuid().ToString("N");
        var critical = await FileOne(requester, $"Server {word} is down", "Critical");
        await FileOne(requester, $"Server {word} is slow", "Low");
        var agent = await factory.ClientFor("agent@yasda.local");

        var found = await agent.GetFromJsonAsync<QueuePage>($"/api/tickets?search={word}&priority=Critical");

        found!.TotalCount.Should().Be(1);
        found.Items.Should().ContainSingle().Which.Id.Should().Be(critical);
    }

    [Fact]
    public async Task A_search_for_a_percent_sign_matches_it_literally()
    {
        var requester = await factory.ClientFor("requester@yasda.local");
        var word = Guid.NewGuid().ToString("N");
        var literal = await FileOne(requester, $"{word} battery at 50% already");
        await FileOne(requester, $"{word} battery at 500 already");
        var agent = await factory.ClientFor("agent@yasda.local");

        var found = await agent.GetFromJsonAsync<QueuePage>($"/api/tickets?search={Uri.EscapeDataString(word + " battery at 50%")}");

        found!.Items.Should().ContainSingle().Which.Id.Should().Be(literal);
    }

    [Fact]
    public async Task The_queue_pages_and_reports_the_total()
    {
        var requester = await factory.ClientFor("requester@yasda.local");
        var word = Guid.NewGuid().ToString("N");
        for (var i = 1; i <= 3; i++)
            await FileOne(requester, $"Paged {word} number {i}");
        var agent = await factory.ClientFor("agent@yasda.local");

        var first = await agent.GetFromJsonAsync<QueuePage>($"/api/tickets?search={word}&pageSize=2&sort=id");
        var second = await agent.GetFromJsonAsync<QueuePage>($"/api/tickets?search={word}&pageSize=2&sort=id&page=2");

        first!.TotalCount.Should().Be(3);
        first.Items.Should().HaveCount(2);
        second!.Items.Should().HaveCount(1);
        first.Items.Select(t => t.Id).Should().NotIntersectWith(second.Items.Select(t => t.Id));
    }
```

| Test | The edge | Why it matters |
|---|---|---|
| `The_ticket_page_shows_the_ticket_and_its_first_message` | A new ticket | The round trip: file it, read it back |
| `A_ticket_that_does_not_exist_is_404` | No such id | A missing thing is a 404, with no exception |
| `Staff_see_everyones_tickets_and_a_requester_only_their_own` | Whose | The privacy rule, in the list and in the page |
| `The_queue_filters_by_priority_and_by_search_text` | Two filters at once | The `WHERE` lines combine |
| `A_search_for_a_percent_sign_matches_it_literally` | `%` in a search | `LIKE`'s own wildcards are escaped |
| `The_queue_pages_and_reports_the_total` | Page 2, and no overlap | `OFFSET`, `LIMIT` and `count_tickets` agree |

Lesson 07's browser-less version of `A_requester_files_a_ticket_and_it_is_saved_with_their_name` stays: it checks the
table itself, and the new tests check the API.

Add two more rows to the door test, so a ticket address is checked too. In `AuthTests`:

```csharp
    [InlineData("/api/auth/me")]
    [InlineData("/api/tickets")]
```

Two old tests also change. `Nothing_of_ours_lands_in_the_public_schema` (lesson 05) learns to check functions as well as
tables, because a function with no schema in front of it would land in `public`:

```csharp
[Fact]
public async Task Nothing_of_ours_lands_in_the_public_schema()
{
    var tables = await Column("SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'");
    var functions = await Column(
        "SELECT p.proname FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'public'");

    tables.Should().BeEmpty();
    functions.Should().BeEmpty("read functions are schema-qualified in their definition");
}
```

### Sorting, and its allow-list

```csharp
// tests/Yasda.Integration.Tests/ListSortingTests.cs
using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;

namespace Yasda.Integration.Tests;

// Every list sorts on the server, by an allow-listed column; the SQL never builds an ORDER BY from the text.
[Collection("Integration")]
public class ListSortingTests(CustomWebAppFactory factory)
{
    private sealed record TicketRow(long Id, string Priority);
    private sealed record Page<T>(List<T> Items, int TotalCount);

    // Priority sorts by urgency, not by spelling: Critical > High > Normal > Low.
    [Fact]
    public async Task Tickets_sort_by_urgency_not_alphabetically()
    {
        var agent = await factory.ClientFor("agent@yasda.local");

        var rows = (await agent.GetFromJsonAsync<Page<TicketRow>>("/api/tickets?sort=priority&desc=true&pageSize=100"))!.Items;

        string[] order = ["Critical", "High", "Normal", "Low"];
        rows.Select(r => Array.IndexOf(order, r.Priority)).Should().BeInAscendingOrder();
    }

    [Theory]
    [InlineData("/api/tickets?sort=requesterId")]      // not a column the list sorts by
    [InlineData("/api/tickets?sort=Subject")]          // the names are exact, the way the JSON writes them
    [InlineData("/api/tickets?sort=id;DROP TABLE tickets.ticket")]
    [InlineData("/api/tickets?status=open")]           // statuses are exact names too
    [InlineData("/api/tickets?page=0")]
    [InlineData("/api/tickets?pageSize=101")]
    public async Task A_sort_the_list_does_not_offer_is_a_400(string url)
    {
        var admin = await factory.ClientFor("admin@yasda.local");

        var response = await admin.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/api/tickets?sort=priority&desc=true")]
    [InlineData("/api/tickets?sort=assigneeName")]
    [InlineData("/api/tickets?sort=requesterName&desc=true")]
    [InlineData("/api/tickets?sort=updatedUtc&status=New&assigned=unassigned")]
    public async Task Every_offered_sort_runs(string url)
    {
        var admin = await factory.ClientFor("admin@yasda.local");

        var response = await admin.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

- **`Tickets_sort_by_urgency_not_alphabetically`** is the one that proves `priority_rank` earns its place.
- **`A_sort_the_list_does_not_offer_is_a_400`** is a `[Theory]` of everything that must be refused: a column not on
  the list, the wrong case, a `DROP TABLE`, a status in the wrong case, a page of zero, a page size of 101.
- **`Every_offered_sort_runs`** runs each sort the page offers, so a typo in a `CASE` can't hide.

Make the allow-list fail once. In `SearchTicketsValidator`, delete the `Sort` rule and run these tests:

```
failed Yasda.Integration.Tests.ListSortingTests.A_sort_the_list_does_not_offer_is_a_400(url: "/api/tickets?sort=requesterId")
  Expected response.StatusCode to be HttpStatusCode.BadRequest {value: 400}, but found HttpStatusCode.OK {value: 200}.
```

Notice what happened: the request got through, and the SQL's `CASE` ignored the unknown name and used the default order. The
`CASE` kept it harmless, and the validator keeps it honest. Put the rule back. Then make the privacy rule fail: in the
handler, remove the line that sets `requesterId` for a non-handler:

```
failed Yasda.Integration.Tests.TicketTests.Staff_see_everyones_tickets_and_a_requester_only_their_own
  Expected myQueue!.Items.Select(t => t.Id) {...} to not contain 76L.
```

Put it back.

### Reads and writes stay apart

One architecture test, with three small helpers:

```csharp
    // CQRS: a command changes state through the domain and EF Core; a query reads with Dapper and SQL functions. A query
    // handler holding a DbContext could write; a command handler using Dapper would skip the domain's rules.
    [Fact]
    public void Queries_read_with_Dapper_and_commands_write_with_EF_Core()
    {
        foreach (var module in Modules)
        {
            foreach (var handler in module.GetTypes())
            {
                if (!IsFeatureHandler(handler)) continue;

                var messageNames = new List<string>();
                foreach (var method in handler.GetMethods())
                {
                    if (method.Name == "HandleAsync")
                        messageNames.Add(method.GetParameters()[0].ParameterType.Name);
                }

                var dependencies = new List<Type>();
                foreach (var parameter in handler.GetConstructors().Single().GetParameters())
                    dependencies.Add(parameter.ParameterType);

                if (IsQueryHandler(handler, messageNames))
                {
                    var dbContexts = new List<string>();
                    foreach (var dependency in dependencies)
                    {
                        if (dependency.IsSubclassOf(typeof(Microsoft.EntityFrameworkCore.DbContext)))
                            dbContexts.Add(dependency.Name);
                    }
                    dbContexts.Should().BeEmpty($"{handler.Name} is a query: it reads with Dapper, never through a DbContext");
                }
                else if (EndsWithAny(messageNames, "Command"))
                {
                    dependencies.Should().NotContain(typeof(IDbConnectionFactory),
                        $"{handler.Name} is a command: it writes through the DbContext and the domain, never with Dapper");
                }
            }
        }
    }
```

```csharp
    private static bool IsFeatureHandler(Type type) =>
        type.Name.EndsWith("Handler", StringComparison.Ordinal) && IsInNamespace(type, ".Features");

    private static bool IsQueryHandler(Type handler, List<string> messageNames)
    {
        if (EndsWithAny(messageNames, "Query")) return true;
        if (handler.Name.StartsWith("Get", StringComparison.Ordinal)) return true;
        if (handler.Name.StartsWith("List", StringComparison.Ordinal)) return true;
        if (handler.Name.StartsWith("Search", StringComparison.Ordinal)) return true;
        return false;
    }

    private static bool EndsWithAny(List<string> names, string suffix)
    {
        foreach (var name in names)
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal)) return true;
        }
        return false;
    }
```

(Add `using Yasda.Shared.BuildingBlocks;` to the file.) It reads every feature handler: if it handles a *query*, it must not
take a `DbContext`; if it handles a *command*, it must not take the connection factory. A query that could write, or a
command that skipped the domain, fails the build. Make it fail once: give `SearchTicketsHandler` a second constructor parameter
of type `TicketsDbContext`:

```
Expected dbContexts to be empty because SearchTicketsHandler is a query: it reads with Dapper, never through a DbContext,
but found at least one item {"TicketsDbContext"}.
```

Remove it.

### In the browser

Lesson 07's browser test of the form changes, because filing now lands on the ticket. Replace it, and add two more.
The whole file is now:

```csharp
// tests/Yasda.Web.UiTests/TicketUiTests.cs
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Yasda.Web.UiTests;

public sealed class TicketUiTests : UiTest
{
    private static ILocator Field(IPage page, string label)
    {
        return page.GetByLabel(label, new() { Exact = true });
    }

    // Files a ticket through the form, as a requester would, and returns the ticket page's address.
    private async Task<(IPage Page, string Address)> FileTicket(string subject)
    {
        var page = await SignIn("requester@yasda.local");
        await MainNav(page).GetByRole(AriaRole.Link, new() { Name = "New ticket" }).ClickAsync();
        await Field(page, "Subject").FillAsync(subject);
        await Field(page, "Details").FillAsync("It shows paper jam, but there's no paper stuck.");
        await Field(page, "Priority").SelectOptionAsync("High");
        await page.GetByRole(AriaRole.Button, new() { Name = "Send ticket" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = subject })).ToBeVisibleAsync();
        await Expect(page.GetByText("Status: New")).ToBeVisibleAsync();
        return (page, new Uri(page.Url).PathAndQuery);
    }

    [UiFact]
    public async Task A_requester_files_a_ticket_and_lands_on_it()
    {
        var (page, _) = await FileTicket($"Printer {Guid.NewGuid():N}");

        await Expect(page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Ticket sent. We'll answer here." })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Article)).ToContainTextAsync("there's no paper stuck");
    }

    [UiFact]
    public async Task The_home_page_offers_to_submit_a_ticket()
    {
        var page = await SignIn("requester@yasda.local");

        await page.GetByRole(AriaRole.Link, new() { Name = "Submit a ticket" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "New ticket" })).ToBeVisibleAsync();
    }

    // The link in an error message must work even for someone whose session just ended, so the page is public.
    [UiFact]
    public async Task The_error_code_page_explains_a_code_to_anyone()
    {
        var page = await NewPage();

        await page.GotoAsync("/errors/TICKETS-0005");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "TICKETS-0005: This ticket is closed" })).ToBeVisibleAsync();
        await Expect(page.GetByText("What to do:")).ToBeVisibleAsync();
    }

    [UiFact]
    public async Task The_queue_filters_through_its_address()
    {
        var page = await SignIn("agent@yasda.local");
        await page.GotoAsync("/tickets");

        await Field(page, "Assigned to").SelectOptionAsync("Nobody yet");
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply" }).ClickAsync();

        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("assigned=unassigned"));
        await Expect(page.GetByText("Showing: assigned to nobody yet.")).ToBeVisibleAsync();
        var assigneeCells = page.GetByRole(AriaRole.Row).Locator("td:nth-child(6)");
        await Expect(assigneeCells.First).ToHaveTextAsync("Nobody yet");
    }

    [UiFact]
    public async Task A_column_heading_sorts_the_list_through_the_address()
    {
        var page = await SignIn("agent@yasda.local");
        await page.GotoAsync("/tickets");

        await page.GetByRole(AriaRole.Columnheader, new() { Name = "Priority" }).GetByRole(AriaRole.Link).ClickAsync();
        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("sort=priority"));
        await Expect(page.GetByRole(AriaRole.Columnheader, new() { Name = "Priority" })).ToHaveAttributeAsync("aria-sort", "ascending");

        await page.GetByRole(AriaRole.Columnheader, new() { Name = "Priority" }).GetByRole(AriaRole.Link).ClickAsync();
        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("desc=true"));
        await Expect(page.GetByRole(AriaRole.Columnheader, new() { Name = "Priority" })).ToHaveAttributeAsync("aria-sort", "descending");
    }
}
```

`FileTicket` is a helper for later lessons too: it signs in as a requester, fills the form, and waits for the new
ticket's page. `A_column_heading_sorts_the_list_through_the_address` clicks the same heading twice and checks the address
and `aria-sort` each time. And one test in `SignInUiTests`, added after the others, makes sure the queue doesn't scroll the
page sideways on a 320-pixel phone:

```csharp
    // The hard case: the full navigation wrapping under the logo, and a wide table, which scrolls in its own box.
    [UiFact]
    public async Task A_list_fits_a_phone_without_sideways_scrolling()
    {
        var page = await SignIn("admin@yasda.local", width: 320);
        await page.GotoAsync("/tickets");
        await Expect(page.GetByRole(AriaRole.Region, new() { Name = "Tickets" })).ToBeVisibleAsync();

        var wider = await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > document.documentElement.clientWidth");

        Assert.False(wider, "the page is wider than the phone");
    }
```

### Edge cases covered in this lesson

| Test | The edge | Why it matters |
|---|---|---|
| `The_queue_filters_through_its_address` | A filter, in a browser | The filter ends up in the address, and the page reads it back |
| `A_sort_the_list_does_not_offer_is_a_400` | A `DROP TABLE`, a wrong case, a page of 0 | Two locks on one door |
| `Queries_read_with_Dapper_and_commands_write_with_EF_Core` | A query holding a `DbContext` | Reads and writes stay in their own tools |
| `Nothing_of_ours_lands_in_the_public_schema` | A function with no schema | Functions belong to a module, like tables |
| `A_list_fits_a_phone_without_sideways_scrolling` | 320 pixels, a wide table | The table scrolls in its box, the page doesn't |

## How a request flows

```mermaid
sequenceDiagram
  participant B as Browser (/tickets?status=New&sort=priority)
  participant F as Validation filter
  participant H as SearchTicketsHandler
  participant D as Dapper
  participant P as PostgreSQL
  B->>F: GET /api/tickets?status=New&sort=priority&desc=true
  F-->>B: 400 + YASDA-0001 (a sort not on the list)
  F->>H: SearchTicketsQuery
  Note over H: a requester? then RequesterId = their id
  H->>D: two statements, one trip
  D->>P: SELECT * FROM tickets.search_tickets(...); SELECT tickets.count_tickets(...)
  P-->>D: the page of rows, and the total
  D-->>H: TicketSummaryDto rows, 3
  H-->>B: { items, page, pageSize, totalCount }
```

## Mistakes to avoid

- Building an `ORDER BY` or a `WHERE` from request text. Pass parameters; let `CASE` pick the order.
- Letting the browser sort a page of results. It sorts only the rows it was given, so the order is wrong across pages.
- Editing a released `.sql` file or its migration. Add a `.v2` and a new migration.
- Returning `403` for a ticket someone may not see. Say `404`.
- Putting a list's state in a component field. Put it in the address, so Back, bookmarks and pasted links work.
- Using EF Core to read a list, or Dapper to save a ticket. The architecture test will tell you.
- A search box that treats `%` and `_` as wildcards.
- A table that makes the whole page scroll sideways on a phone.
- Naming a SQL function without its schema. It lands in `public`.

## Let's run it!

```bash
dotnet build
dotnet test
```

The build shows **0 warnings and 0 errors**, and the tests report 106 passed and 13 skipped (the browser tests). Start the
app and run those too:

```bash
dotnet run --project src/Yasda.Api
```

```bash
YASDA_UI_URL=http://localhost:5264 dotnet test tests/Yasda.Web.UiTests
```

Thirteen passed. Look at the functions PostgreSQL now holds for you:

```bash
psql "host=localhost dbname=yasda user=yasda_dev password=yasda_dev_only" -c "\df tickets.*"
```

```
 Schema  |        Name         | Result data type | ...
---------+---------------------+------------------+-----
 tickets | count_tickets       | bigint           | ...
 tickets | get_ticket          | TABLE(...)       | ...
 tickets | get_ticket_messages | TABLE(...)       | ...
 tickets | priority_rank       | integer          | ...
 tickets | search_tickets      | TABLE(...)       | ...
```

Save the moment in Git:

```bash
git add .
git commit -m "Lesson 08: Dapper, SQL functions, the queue and the ticket page"
```

## Let's check!

1. Why do reads use Dapper while writes use EF Core? Give one cost of EF Core that a list doesn't need.
2. What is SQL injection, and which two things in this lesson stop a `?sort=` from becoming one?
3. How does a requester end up seeing only their own tickets, and where is that rule written?
4. Why does the API answer `404`, not `403`, for someone else's ticket?
5. Why must `ValidSort` list the names *exactly*, and what would it let through if it ignored case?
6. Why does the queue keep its filters, sort and page in the address instead of in the page's fields?
7. A teammate wants to add a "team" filter to `search_tickets`. What do they create, and what do they leave alone?
8. Why does `search_tickets` end its `ORDER BY` with `t.id DESC`?

And that's where this lesson ends. Agents have a queue that filters, sorts and pages through the address, every ticket has a page,
and the reads are fast, safe and tested. Next time, the first thing an agent *does* with a ticket: take it, and what happens
when two agents click at the same moment. Good luck, developer!

---

← [Lesson 07 — File a ticket](07-file-a-ticket.md) · Next: Lesson 09 — Take a ticket (coming with milestone Y2) →
