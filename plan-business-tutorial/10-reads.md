# Lesson 10 — Reads: Dapper and SQL Functions

Good day, developer! So far, every feature has *changed* something. Today we read. A screen showing one order
in full, and a list of orders you can filter and page through. Reading looks easy, and that's exactly why it
gets done badly, so we'll use a different tool for it, one that lets the database do what it's best at. About
an hour and a half. Are you ready? Let's begin!

## What we'll build

- `GET /sales-orders/{id}`: one order, with its customer's name, its lines and their item names.
- `GET /sales-orders?customerId=&status=&page=&pageSize=`: a filtered, paged list, newest first.
- Four **SQL functions** in PostgreSQL that do the reading, shipped to the database by a migration.
- **Dapper**, a small library that runs SQL and turns the rows into C# objects.

## Why we build it this way

### Why not EF Core for reads too?

You could load orders with EF Core, the way the command handlers do. For a list of 20 orders, that means EF
loads 20 full `SalesOrder` aggregates, with every line of every order, tracks all of them for changes that
will never come, and then you copy them into the shape the screen wants. Meanwhile the screen wanted 6
columns and a total.

A read wants a **shape**, not an aggregate. SQL is the best language there is for "join these, sum that,
filter, sort, give me page 3". So reads skip the domain entirely:

| | Commands (writes) | Queries (reads) |
|---|---|---|
| Goal | Change data safely | Show data quickly |
| Goes through | The domain, with all its rules | Straight to the database |
| Tool | EF Core | Dapper + SQL functions |
| Returns | Usually nothing, or an id | A shape made for one screen |

This is the "query" half of CQRS-lite, and it's where it pays off.

### Why functions in the database

We could write the `SELECT` statements as strings inside C#. Putting them in the database as **functions**
instead has three benefits: a database administrator can read and tune them without touching C#, PostgreSQL
checks them when they're created, and every caller runs exactly the same query.

You might wonder why functions and not stored procedures. In PostgreSQL, a procedure can't hand back rows;
a function can. So a function declared `RETURNS TABLE (...)` is how PostgreSQL does "a stored query".

### Shipped by a migration, never edited

The functions live as `.sql` files in the module, and a migration creates them. That way, every database that
has the tables also has the queries; you can't forget to run a script. And one rule keeps that safe: **once a
script has been released, it's never edited.** A changed function gets a new file, `.v2.sql`, and a new
migration. Otherwise, a fresh database would get the *new* function from the *old* migration, and the two
would quietly disagree.

## 1. Dapper, and a connection for each read

```bash
dotnet add src/Modules/Sales/Erp.Modules.Sales package Dapper --version 2.1.79
dotnet add src/Shared/Erp.Shared.Infrastructure package Dapper --version 2.1.79
```

Dapper (Apache-2.0) adds methods like `QueryAsync<T>` to any database connection. You give it SQL and
parameters; it gives you back objects. Infrastructure needs it for one setting, coming up in a moment.

A read doesn't need the request's shared connection; it's not part of any transaction. It just needs a
connection of its own, briefly. In BuildingBlocks' `Data.cs`:

```csharp
// A new connection per Dapper read; the caller disposes it.
public interface IDbConnectionFactory
{
    DbConnection Create();
}
```

In Infrastructure's `PostgresConnections.cs`:

```csharp
public sealed class PostgresConnectionFactory(NpgsqlDataSource dataSource) : IDbConnectionFactory
{
    public DbConnection Create() => dataSource.CreateConnection();
}
```

And in `AddSharedInfrastructure`:

```csharp
services.AddSingleton<IDbConnectionFactory, PostgresConnectionFactory>();

// PostgreSQL names columns in snake_case (order_number); our DTOs use PascalCase (OrderNumber).
// This tells Dapper that the two are the same name, for every read query in every module.
Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
```

That last line is why Infrastructure needed Dapper: it's a global setting, so it's set once, in one place.

A paged list needs a standard envelope too: the items, plus enough to draw the page buttons. In
BuildingBlocks:

```csharp
// src/Shared/Erp.Shared.BuildingBlocks/Abstractions/PagedResult.cs
namespace Erp.Shared.BuildingBlocks;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}
```

## 2. The SQL functions

Create the folder `src/Modules/Sales/Erp.Modules.Sales/Infrastructure/Sql/Functions/`. The `.sql` files are
already embedded by the `EmbeddedResource` line from [lesson 04](04-database.md). One function per file,
named `schema.function.vN.sql`.

```sql
-- sales.get_sales_order.v1.sql
-- Released scripts are never edited: change a function with a .v2 file and a new migration.
CREATE OR REPLACE FUNCTION sales.get_sales_order(p_order_id bigint)
RETURNS TABLE (id bigint, order_number text, customer_id bigint, customer_name text, status integer,
               total numeric, created_utc timestamptz, submitted_utc timestamptz,
               approved_by bigint, approved_utc timestamptz, rejection_reason text)
LANGUAGE sql STABLE
AS $$
    SELECT o.id, o.order_number::text, o.customer_id, c.name::text, o.status,
           COALESCE((SELECT SUM(l.quantity * l.unit_price)
                     FROM sales.sales_order_line l WHERE l.sales_order_id = o.id), 0),
           o.created_utc, o.submitted_utc, o.approved_by, o.approved_utc, o.rejection_reason::text
    FROM sales.sales_order o
    JOIN sales.customer c ON c.id = o.customer_id
    WHERE o.id = p_order_id;
$$;
```

```sql
-- sales.get_sales_order_lines.v1.sql
-- Released scripts are never edited: change a function with a .v2 file and a new migration.
CREATE OR REPLACE FUNCTION sales.get_sales_order_lines(p_order_id bigint)
RETURNS TABLE (item_id bigint, item_name text, quantity integer, unit_price numeric)
LANGUAGE sql STABLE
AS $$
    SELECT l.item_id, i.name::text, l.quantity, l.unit_price
    FROM sales.sales_order_line l
    JOIN sales.item i ON i.id = l.item_id
    WHERE l.sales_order_id = p_order_id
    ORDER BY l.id;
$$;
```

```sql
-- sales.search_sales_orders.v1.sql
-- Released scripts are never edited: change a function with a .v2 file and a new migration.
CREATE OR REPLACE FUNCTION sales.search_sales_orders(
    p_customer_id bigint, p_status integer, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, order_number text, customer_name text, status integer, total numeric, created_utc timestamptz)
LANGUAGE sql STABLE
AS $$
    SELECT o.id, o.order_number::text, c.name::text, o.status,
           COALESCE((SELECT SUM(l.quantity * l.unit_price)
                     FROM sales.sales_order_line l WHERE l.sales_order_id = o.id), 0),
           o.created_utc
    FROM sales.sales_order o
    JOIN sales.customer c ON c.id = o.customer_id
    WHERE (p_customer_id IS NULL OR o.customer_id = p_customer_id)
      AND (p_status      IS NULL OR o.status      = p_status)
    ORDER BY o.created_utc DESC, o.id DESC
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;
```

```sql
-- sales.count_sales_orders.v1.sql
-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- Separate from the search so the count is right even for a page past the end.
CREATE OR REPLACE FUNCTION sales.count_sales_orders(p_customer_id bigint, p_status integer)
RETURNS bigint
LANGUAGE sql STABLE
AS $$
    SELECT COUNT(*)
    FROM sales.sales_order o
    WHERE (p_customer_id IS NULL OR o.customer_id = p_customer_id)
      AND (p_status      IS NULL OR o.status      = p_status);
$$;
```

A few things worth knowing in there:

| You see | It means |
|---|---|
| `LANGUAGE sql STABLE` | A plain SQL function that only reads, so PostgreSQL may optimize it freely |
| `::text` | Converts `varchar` to `text`, because the declared result types must match exactly |
| The total, computed with `SUM` | Same as the domain: the total is never stored, so it can never be wrong |
| `(p_status IS NULL OR ...)` | A filter that's skipped when the caller didn't ask for it |
| `OFFSET ... LIMIT ...` | Skip the earlier pages, take one page |
| A separate count function | So a page past the end still reports the real total, not zero |

Now generate an empty migration, and fill it in by hand:

```bash
dotnet ef migrations add AddReadFunctions \
  --project src/Modules/Sales/Erp.Modules.Sales --startup-project src/Modules/Sales/Erp.Modules.Sales \
  --output-dir Infrastructure/Migrations
```

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Infrastructure/Migrations/<timestamp>_AddReadFunctions.cs
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Sales.Infrastructure.Migrations
{
    // The read-side SQL functions ship with the schema. Never point a released migration at a changed file.
    public partial class AddReadFunctions : Migration
    {
        private static string Sql(string file) => EmbeddedSql.Read(typeof(AddReadFunctions).Assembly, file);

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Sql("sales.get_sales_order.v1.sql"));
            migrationBuilder.Sql(Sql("sales.get_sales_order_lines.v1.sql"));
            migrationBuilder.Sql(Sql("sales.search_sales_orders.v1.sql"));
            migrationBuilder.Sql(Sql("sales.count_sales_orders.v1.sql"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales.count_sales_orders(bigint, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales.search_sales_orders(bigint, integer, integer, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales.get_sales_order_lines(bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales.get_sales_order(bigint);");
        }
    }
}
```

`Up` creates the functions; `Down` removes them, in case you ever roll this migration back.

## 3. Get one order

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Features/GetSalesOrder/GetSalesOrderHandler.cs
using Dapper;
using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;
using Mediator;

namespace Erp.Modules.Sales.Features;

internal sealed record GetSalesOrderQuery(long OrderId) : IQuery<SalesOrderDetailDto?>;

internal sealed record SalesOrderDetailDto(
    long Id, string OrderNumber, long CustomerId, string CustomerName, string Status, decimal Total,
    DateTime CreatedUtc, DateTime? SubmittedUtc, long? ApprovedBy, DateTime? ApprovedUtc, string? RejectionReason,
    IReadOnlyList<SalesOrderLineDto> Lines);

internal sealed record SalesOrderLineDto(long ItemId, string ItemName, int Quantity, decimal UnitPrice, decimal LineTotal);

internal sealed record SalesOrderHeaderRow(
    long Id, string OrderNumber, long CustomerId, string CustomerName, int Status, decimal Total,
    DateTime CreatedUtc, DateTime? SubmittedUtc, long? ApprovedBy, DateTime? ApprovedUtc, string? RejectionReason);
internal sealed record SalesOrderLineRow(long ItemId, string ItemName, int Quantity, decimal UnitPrice);

internal sealed class GetSalesOrderHandler(IDbConnectionFactory connections)
    : IQueryHandler<GetSalesOrderQuery, SalesOrderDetailDto?>
{
    public async ValueTask<SalesOrderDetailDto?> Handle(GetSalesOrderQuery q, CancellationToken ct)
    {
        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            "SELECT * FROM sales.get_sales_order(@OrderId); SELECT * FROM sales.get_sales_order_lines(@OrderId);",
            new { q.OrderId }, cancellationToken: ct));

        var h = await multi.ReadSingleOrDefaultAsync<SalesOrderHeaderRow>();
        if (h is null) return null;
        var lines = (await multi.ReadAsync<SalesOrderLineRow>())
            .Select(l => new SalesOrderLineDto(l.ItemId, l.ItemName, l.Quantity, l.UnitPrice, l.Quantity * l.UnitPrice))
            .ToList();

        return new SalesOrderDetailDto(
            h.Id, h.OrderNumber, h.CustomerId, h.CustomerName, ((SalesOrderStatus)h.Status).ToString(), h.Total,
            h.CreatedUtc, h.SubmittedUtc, h.ApprovedBy, h.ApprovedUtc, h.RejectionReason, lines);
    }
}
```

There are two kinds of records here, and the difference matters:

- **`...Row` records** match the function's columns exactly. Dapper fills them. (`order_number` fills
  `OrderNumber`, thanks to `MatchNamesWithUnderscores`.)
- **`...Dto` records** are what the caller receives. Here the status becomes a readable word like
  `"Approved"`, and each line gets its line total.

`QueryMultipleAsync` sends both `SELECT`s in **one round trip** and reads two result sets back.
`@OrderId` is a parameter, filled from `new { q.OrderId }`, never pasted into the SQL text, so SQL
injection can't happen.

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Features/GetSalesOrder/GetSalesOrderEndpoint.cs
using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed class GetSalesOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet("/sales-orders/{id:long}",
            async (long id, ISender sender, CancellationToken ct) =>
            {
                var dto = await sender.Send(new GetSalesOrderQuery(id), ct);
                return dto is null ? Results.NotFound() : Results.Ok(dto);
            })
            .RequireAuthorization();   // any signed-in user
}
```

## 4. Search

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Features/SearchSalesOrders/SearchSalesOrdersHandler.cs
using Dapper;
using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;

namespace Erp.Modules.Sales.Features;

internal sealed record SearchSalesOrdersQuery(long? CustomerId, string? Status, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<SalesOrderSummaryDto>>;

internal sealed record SalesOrderSummaryDto(
    long Id, string OrderNumber, string CustomerName, string Status, decimal Total, DateTime CreatedUtc);

internal sealed record SalesOrderSummaryRow(
    long Id, string OrderNumber, string CustomerName, int Status, decimal Total, DateTime CreatedUtc);

internal sealed class SearchSalesOrdersValidator : AbstractValidator<SearchSalesOrdersQuery>
{
    public SearchSalesOrdersValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Status)
            .Must(s => s is null || Enum.TryParse<SalesOrderStatus>(s, ignoreCase: true, out _))
            .WithMessage(x => $"Unknown status '{x.Status}'.");
    }
}

internal sealed class SearchSalesOrdersHandler(IDbConnectionFactory connections)
    : IQueryHandler<SearchSalesOrdersQuery, PagedResult<SalesOrderSummaryDto>>
{
    public async ValueTask<PagedResult<SalesOrderSummaryDto>> Handle(SearchSalesOrdersQuery q, CancellationToken ct)
    {
        int? status = q.Status is null ? null : (int)Enum.Parse<SalesOrderStatus>(q.Status, ignoreCase: true);

        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM sales.search_sales_orders(@CustomerId, @Status, @Page, @PageSize);
            SELECT sales.count_sales_orders(@CustomerId, @Status);
            """,
            new { q.CustomerId, Status = status, q.Page, q.PageSize }, cancellationToken: ct));

        var items = (await multi.ReadAsync<SalesOrderSummaryRow>())
            .Select(r => new SalesOrderSummaryDto(
                r.Id, r.OrderNumber, r.CustomerName, ((SalesOrderStatus)r.Status).ToString(), r.Total, r.CreatedUtc))
            .ToList();
        var total = await multi.ReadSingleAsync<long>();

        return new PagedResult<SalesOrderSummaryDto>(items, q.Page, q.PageSize, (int)total);
    }
}
```

Queries go through the validation behavior too. Here it caps the page size at 100, so nobody can ask for a
million rows, and rejects a status that doesn't exist with a clear 400 instead of an empty list.

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Features/SearchSalesOrders/SearchSalesOrdersEndpoint.cs
using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed class SearchSalesOrdersEndpoint : ICarterModule
{
    // int?, not int: a non-nullable query parameter is required, so a bare GET would be a 400.
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet("/sales-orders",
            async (long? customerId, string? status, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(
                    new SearchSalesOrdersQuery(customerId, status, page ?? 1, pageSize ?? 20), ct)))
            .RequireAuthorization();
}
```

That comment points at a real trap. Minimal APIs treat a query parameter declared as plain `int` as
*required*. So `GET /sales-orders`, with no `?page=`, would fail with a 400 before your handler ever ran. Make
it `int?` and give it a default yourself.

## How a request flows

```mermaid
sequenceDiagram
  participant C as Client
  participant H as SearchSalesOrdersHandler
  participant PG as PostgreSQL
  C->>H: GET /sales-orders?status=Approved&page=1
  H->>PG: SELECT * FROM search_sales_orders(...); SELECT count_sales_orders(...)
  PG-->>H: 20 rows + total
  H-->>C: { items, page, pageSize, totalCount, totalPages }
```

No domain, no change tracking, no transaction. One trip to the database. Easy, right?

## Mistakes to avoid

- Loading aggregates with EF Core to fill a list screen. Use a read query shaped for the screen.
- Pasting values into SQL strings. Always use parameters (`@OrderId`).
- Editing a released `.sql` file. Add `.v2` and a new migration.
- Storing a total to make reads faster. Compute it; it's never wrong that way.
- Declaring an optional query parameter as plain `int`.

## Let's run it!

Restart the app so the new migration runs. Then, with a token from any user:

```bash
curl -s http://localhost:5264/sales-orders/1 -H "Authorization: Bearer $TOKEN" | jq
curl -s "http://localhost:5264/sales-orders?status=Approved&pageSize=5" -H "Authorization: Bearer $TOKEN" | jq
curl -s "http://localhost:5264/sales-orders?page=9999" -H "Authorization: Bearer $TOKEN" | jq '{items, totalCount}'
# { "items": [], "totalCount": 12 }   (your count will differ — but it's not 0)
curl -s "http://localhost:5264/sales-orders?status=Shipped" -H "Authorization: Bearer $TOKEN" | jq .errors
# [ "Unknown status 'Shipped'." ]
```

You can also call a function straight from `psql`, which is handy when you're tuning one:

```bash
PGPASSWORD=erp_dev_only "/c/Program Files/PostgreSQL/18/bin/psql.exe" -h localhost -U postgres -d erp \
  -c "SELECT * FROM sales.search_sales_orders(NULL, NULL, 1, 5);"
```

## Let's check!

1. Why don't the query handlers load `SalesOrder` aggregates with EF Core?
2. Why are the reads PostgreSQL *functions* rather than stored procedures?
3. A function needs a new column. What files do you add, and what must you never do?
4. What is `MatchNamesWithUnderscores` for, and why is it set in Infrastructure?
5. Why is the order count a separate function from the search?
6. What happens on `GET /sales-orders` if `page` is declared as `int` instead of `int?`?

And that's where this lesson ends. Every write goes through the domain, and every read goes straight to a
query built for it. Next time, we make the system *react*: when an order is approved, something else
happens, reliably, even if the server crashes at the worst moment. Good luck, developer!

---

← [Lesson 09 — Update an order](09-update-order.md) · Next: [Lesson 11 — Events and the outbox](11-events-outbox.md) →
