# Lesson 07 — Find Orders: Dapper, SQL Functions, and Who Sees What

Good day, developer! Last time we saved our first order, through a form that made you type ids. Today we
read. A list of orders you can search and page through, a page for each order, and search boxes for that
form, so nobody ever types an id again. Reading looks easy, and that's exactly why it gets done badly, so
we'll use a different tool for it, one that lets the database do what it's best at. And we'll settle a
question every ERP must answer: who is allowed to see which orders? About two hours. Are you ready? Let's
begin!

## What we'll build

| Endpoint | What it returns |
|---|---|
| `GET /api/sales-orders?status=&search=&page=&pageSize=` | A filtered, paged list, newest first |
| `GET /api/sales-orders/{id}` | One order, with its customer's name and its lines |
| `GET /api/customers?search=` and `/api/customers/{id}` | Customers, with the credit they have left |
| `GET /api/price-list?search=&forSaleOnly=` | Items Sales can put on an order, with their prices |

Plus the SQL functions behind them, **Dapper** to call them, the rule that a sales rep sees only their own
orders, and three screens: the order list, the order page, and the new-order form with search boxes.

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

This is the "query" half of CQRS-lite, and it's where it pays off. Now, let's be honest about the cost:
the same idea ("an order's total") is now written twice, once in C# and once in SQL. The integration tests
in [lesson 12](12-testing.md) are what keep the two in agreement.

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
would quietly disagree. You'll do exactly that in later lessons, when orders learn to ship and customers
gain a tax number.

### Who sees which orders

A sales rep's orders are their pipeline, and often their commission. Most businesses don't want every rep
reading every other rep's deals. So:

| Role | Sees |
|---|---|
| SalesRep | Only the orders they created |
| SalesManager, Admin | Every order |

And when a rep asks for someone else's order, the answer is **404, not 403**. A 403 would say "that order
exists, you just can't have it", which already leaks something. A 404 says nothing at all.

This is a rule about the business, so it lives in the domain, and every read and write asks the domain.

## 1. Dapper, and a connection for each read

```bash
dotnet add src/Modules/Sales/Erp.Modules.Sales package Dapper --version 2.1.79
dotnet add src/Shared/Erp.Shared.Infrastructure package Dapper --version 2.1.79
```

Dapper (Apache-2.0) adds methods like `QueryAsync<T>` to any database connection. You give it SQL and
parameters; it gives you back objects. Infrastructure needs it for one setting, coming up in a moment.

A read doesn't change anything and doesn't join anyone's transaction. It just needs a connection of its
own, briefly. In BuildingBlocks' `Data.cs`:

```csharp
// A new connection per Dapper read; the caller disposes it.
public interface IDbConnectionFactory
{
    DbConnection Create();
}
```

In Infrastructure:

```csharp
// src/Shared/Erp.Shared.Infrastructure/Data/PostgresConnections.cs
using System.Data.Common;
using Erp.Shared.BuildingBlocks;
using Npgsql;

namespace Erp.Shared.Infrastructure;

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

A paged list needs a standard envelope: the items, plus enough to draw the page buttons. In BuildingBlocks:

```csharp
// src/Shared/Erp.Shared.BuildingBlocks/Abstractions/PagedResult.cs
namespace Erp.Shared.BuildingBlocks;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}
```

And every list validates its paging the same way, so add two rules next to `ValidQuantity` in
`ValidationRules`:

```csharp
public static IRuleBuilderOptions<T, int> ValidPage<T>(this IRuleBuilder<T, int> rule) => rule.GreaterThan(0);

public static IRuleBuilderOptions<T, int> ValidPageSize<T>(this IRuleBuilder<T, int> rule) => rule.InclusiveBetween(1, 100);
```

The cap of 100 means nobody can ask for a million rows in one request.

## 2. Who sees what, in the domain

Two small methods on `SalesOrder`:

```csharp
// in Domain/SalesOrder.cs, above SetLines

// A sales rep sees only the orders they created; everyone else who may see orders sees them all.
public static UserId? CreatorFilterFor(UserId user, string role) => role == Roles.SalesRep ? user : null;

public bool IsVisibleTo(UserId user, string role) => CreatorFilterFor(user, role) is not { } only || only == CreatedBy;
```

- **`CreatorFilterFor`** answers "whose orders may this person see?" as a filter: a user id ("only these"),
  or `null` ("no filter"). It's `static` because a *list* needs the answer before it has any order in hand.
- **`IsVisibleTo`** asks the same question about one order. It's built on the first, so the two can't
  disagree.

A test in `DomainTests.cs` pins the rule down:

```csharp
public class VisibilityTests
{
    [Fact]
    public void A_rep_sees_only_their_own_orders_and_a_manager_sees_all()
    {
        var order = Make.Order(Make.Customer(), unitPrice: 25, createdBy: Make.Rep);

        order.IsVisibleTo(Make.Rep, Roles.SalesRep).Should().BeTrue();
        order.IsVisibleTo(new UserId(99), Roles.SalesRep).Should().BeFalse();
        order.IsVisibleTo(Make.Manager, Roles.SalesManager).Should().BeTrue();
    }
}
```

The command handlers in the next lessons ask `IsVisibleTo`. The SQL functions below take the filter as a
parameter. Both come from these two lines.

## 3. The SQL functions

Create the folder `src/Modules/Sales/Erp.Modules.Sales/Infrastructure/Sql/Functions/`. The `.sql` files are
already embedded by the `EmbeddedResource` line from [lesson 04](04-database.md). One file per read, named
`schema.function.vN.sql`, holding the function and its companion: the lines for a "get", the count for a
"search".

```sql
-- sales.get_sales_order.v1.sql
-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- p_created_by: NULL = any order; otherwise only that user's (a rep sees only their own).
CREATE OR REPLACE FUNCTION sales.get_sales_order(p_order_id bigint, p_created_by bigint)
RETURNS TABLE (id bigint, order_number text, customer_id bigint, customer_name text, status integer, total numeric,
               created_by bigint, created_utc timestamptz, submitted_utc timestamptz, approved_by bigint,
               approved_utc timestamptz, rejected_by bigint, rejection_reason text)
LANGUAGE sql STABLE
AS $$
    SELECT o.id, o.order_number::text, o.customer_id, c.name::text, o.status,
           COALESCE((SELECT SUM(ROUND(l.quantity * l.unit_price, 2))
                     FROM sales.sales_order_line l WHERE l.sales_order_id = o.id), 0),
           o.created_by, o.created_utc, o.submitted_utc, o.approved_by, o.approved_utc, o.rejected_by,
           o.rejection_reason::text
    FROM sales.sales_order o
    JOIN sales.customer c ON c.id = o.customer_id
    WHERE o.id = p_order_id
      AND (p_created_by IS NULL OR o.created_by = p_created_by);
$$;

CREATE OR REPLACE FUNCTION sales.get_sales_order_lines(p_order_id bigint)
RETURNS TABLE (item_id bigint, item_name text, unit text, quantity numeric, unit_price numeric)
LANGUAGE sql STABLE
AS $$
    SELECT l.item_id, l.item_name::text, l.unit::text, l.quantity, l.unit_price
    FROM sales.sales_order_line l
    WHERE l.sales_order_id = p_order_id
    ORDER BY l.id;
$$;
```

```sql
-- sales.search_sales_orders.v1.sql
-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- p_created_by: NULL = everyone's orders; otherwise only that user's (a rep sees only their own).
CREATE OR REPLACE FUNCTION sales.search_sales_orders(
    p_customer_id bigint, p_status integer, p_created_by bigint, p_search text, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, order_number text, customer_id bigint, customer_name text, status integer, total numeric,
               created_by bigint, created_utc timestamptz)
LANGUAGE sql STABLE
AS $$
    SELECT o.id, o.order_number::text, o.customer_id, c.name::text, o.status,
           COALESCE((SELECT SUM(ROUND(l.quantity * l.unit_price, 2))
                     FROM sales.sales_order_line l WHERE l.sales_order_id = o.id), 0),
           o.created_by, o.created_utc
    FROM sales.sales_order o
    JOIN sales.customer c ON c.id = o.customer_id
    WHERE (p_customer_id IS NULL OR o.customer_id = p_customer_id)
      AND (p_status      IS NULL OR o.status      = p_status)
      AND (p_created_by  IS NULL OR o.created_by  = p_created_by)
      AND (p_search      IS NULL OR o.order_number ILIKE '%' || p_search || '%' OR c.name ILIKE '%' || p_search || '%')
    ORDER BY o.created_utc DESC, o.id DESC
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;

-- Separate from the search so the count is right even for a page past the end.
CREATE OR REPLACE FUNCTION sales.count_sales_orders(p_customer_id bigint, p_status integer, p_created_by bigint, p_search text)
RETURNS bigint
LANGUAGE sql STABLE
AS $$
    SELECT COUNT(*)
    FROM sales.sales_order o
    JOIN sales.customer c ON c.id = o.customer_id
    WHERE (p_customer_id IS NULL OR o.customer_id = p_customer_id)
      AND (p_status      IS NULL OR o.status      = p_status)
      AND (p_created_by  IS NULL OR o.created_by  = p_created_by)
      AND (p_search      IS NULL OR o.order_number ILIKE '%' || p_search || '%' OR c.name ILIKE '%' || p_search || '%');
$$;
```

A few things worth knowing in there:

| You see | It means |
|---|---|
| `LANGUAGE sql STABLE` | A plain SQL function that only reads, so PostgreSQL may optimize it freely |
| `::text` | Converts `varchar` to `text`, because the declared result types must match exactly |
| `SUM(ROUND(quantity * unit_price, 2))` | The same arithmetic as `Money`: each line rounded to centavos, then added |
| `(p_status IS NULL OR ...)` | A filter that's skipped when the caller didn't ask for it |
| `ILIKE '%' \|\| p_search \|\| '%'` | "Contains", ignoring case: `acme` finds Acme Corp, `0042` finds SO-2026-000042 |
| `OFFSET ... LIMIT ...` | Skip the earlier pages, take one page |
| A separate count function | So a page past the end still reports the real total, not zero |

The order form needs two more reads: customers to pick from, with the credit they have left, and the items
Sales may sell.

```sql
-- sales.search_customers.v1.sql
-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- p_customer_id set: that one customer. Pending = submitted orders still waiting for a manager.
CREATE OR REPLACE FUNCTION sales.search_customers(p_customer_id bigint, p_search text, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, name text, is_blocked boolean, credit_limit numeric, outstanding_balance numeric,
               pending_total numeric, available_credit numeric)
LANGUAGE sql STABLE
AS $$
    WITH pending AS (
        SELECT o.customer_id, SUM(ROUND(l.quantity * l.unit_price, 2)) AS total
        FROM sales.sales_order o
        JOIN sales.sales_order_line l ON l.sales_order_id = o.id
        WHERE o.status = 1
        GROUP BY o.customer_id
    )
    SELECT c.id, c.name::text, c.is_blocked, c.credit_limit, c.outstanding_balance, COALESCE(p.total, 0),
           GREATEST(c.credit_limit - c.outstanding_balance - COALESCE(p.total, 0), 0)
    FROM sales.customer c
    LEFT JOIN pending p ON p.customer_id = c.id
    WHERE (p_customer_id IS NULL OR c.id = p_customer_id)
      AND (p_search IS NULL OR c.name ILIKE '%' || p_search || '%')
    ORDER BY c.name, c.id
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;

CREATE OR REPLACE FUNCTION sales.count_customers(p_search text)
RETURNS bigint
LANGUAGE sql STABLE
AS $$
    SELECT COUNT(*) FROM sales.customer c
    WHERE p_search IS NULL OR c.name ILIKE '%' || p_search || '%';
$$;
```

```sql
-- sales.search_price_list.v1.sql
-- Released scripts are never edited: change a function with a .v2 file and a new migration.
CREATE OR REPLACE FUNCTION sales.search_price_list(p_search text, p_for_sale_only boolean, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, sku text, name text, unit text, allows_fractions boolean, is_active boolean,
               unit_price numeric, category_id bigint, category_name text, is_for_sale boolean)
LANGUAGE sql STABLE
AS $$
    SELECT i.id, i.sku::text, i.name::text, i.unit::text, i.allows_fractions, i.is_active,
           i.unit_price, i.category_id, c.name::text, i.is_for_sale
    FROM sales.item i
    LEFT JOIN sales.item_category c ON c.id = i.category_id
    WHERE (p_search IS NULL OR i.sku ILIKE '%' || p_search || '%' OR i.name ILIKE '%' || p_search || '%')
      AND (NOT p_for_sale_only OR (i.is_for_sale AND i.is_active))
    ORDER BY i.name, i.id
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;

CREATE OR REPLACE FUNCTION sales.count_price_list(p_search text, p_for_sale_only boolean)
RETURNS bigint
LANGUAGE sql STABLE
AS $$
    SELECT COUNT(*) FROM sales.item i
    WHERE (p_search IS NULL OR i.sku ILIKE '%' || p_search || '%' OR i.name ILIKE '%' || p_search || '%')
      AND (NOT p_for_sale_only OR (i.is_for_sale AND i.is_active));
$$;
```

Look at `available_credit`. It's the same sum the domain's credit check makes (limit, minus what they owe,
minus what's awaiting approval), so the form can warn a rep *before* they save. The domain still decides
on save; the number on screen is a courtesy.

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
            migrationBuilder.Sql(Sql("sales.search_sales_orders.v1.sql"));
            migrationBuilder.Sql(Sql("sales.search_customers.v1.sql"));
            migrationBuilder.Sql(Sql("sales.search_price_list.v1.sql"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales.count_price_list(text, boolean);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales.search_price_list(text, boolean, integer, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales.count_customers(text);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales.search_customers(bigint, text, integer, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales.count_sales_orders(bigint, integer, bigint, text);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales.search_sales_orders(bigint, integer, bigint, text, integer, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales.get_sales_order_lines(bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales.get_sales_order(bigint, bigint);");
        }
    }
}
```

`Up` creates the functions; `Down` removes them, in case you ever roll this migration back.

## 4. Get one order

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
    long CreatedBy, DateTime CreatedUtc, DateTime? SubmittedUtc, long? ApprovedBy, DateTime? ApprovedUtc,
    long? RejectedBy, string? RejectionReason, IReadOnlyList<SalesOrderLineDto> Lines);

internal sealed record SalesOrderLineDto(long ItemId, string ItemName, string Unit, decimal Quantity, decimal UnitPrice, decimal LineTotal);

internal sealed record SalesOrderHeaderRow(
    long Id, string OrderNumber, long CustomerId, string CustomerName, int Status, decimal Total, long CreatedBy,
    DateTime CreatedUtc, DateTime? SubmittedUtc, long? ApprovedBy, DateTime? ApprovedUtc, long? RejectedBy,
    string? RejectionReason);

internal sealed record SalesOrderLineRow(long ItemId, string ItemName, string Unit, decimal Quantity, decimal UnitPrice);

internal sealed class GetSalesOrderHandler(IDbConnectionFactory connections, ICurrentUser currentUser)
    : IQueryHandler<GetSalesOrderQuery, SalesOrderDetailDto?>
{
    public async ValueTask<SalesOrderDetailDto?> Handle(GetSalesOrderQuery q, CancellationToken ct)
    {
        var createdBy = SalesOrder.CreatorFilterFor(new UserId(currentUser.UserId), currentUser.Role)?.Value;

        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM sales.get_sales_order(@OrderId, @CreatedBy);
            SELECT * FROM sales.get_sales_order_lines(@OrderId);
            """, new { q.OrderId, CreatedBy = createdBy }, cancellationToken: ct));

        var h = await multi.ReadSingleOrDefaultAsync<SalesOrderHeaderRow>();
        if (h is null) return null;
        var lines = (await multi.ReadAsync<SalesOrderLineRow>())
            .Select(l => new SalesOrderLineDto(l.ItemId, l.ItemName, l.Unit, l.Quantity, l.UnitPrice,
                Math.Round(l.Quantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero)))
            .ToList();

        return new SalesOrderDetailDto(
            h.Id, h.OrderNumber, h.CustomerId, h.CustomerName, ((SalesOrderStatus)h.Status).ToString(), h.Total,
            h.CreatedBy, h.CreatedUtc, h.SubmittedUtc, h.ApprovedBy, h.ApprovedUtc, h.RejectedBy, h.RejectionReason, lines);
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

And look at the first line of `Handle`. A rep's own id goes in as `@CreatedBy`, so someone else's order
simply isn't found. The handler returns `null`, the endpoint turns that into a 404, and the rep learns
nothing.

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
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Sellers));
}
```

## 5. Search orders

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Features/SearchSalesOrders/SearchSalesOrdersHandler.cs
using Dapper;
using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;

namespace Erp.Modules.Sales.Features;

internal sealed record SearchSalesOrdersQuery(long? CustomerId, string? Status, string? Search, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<SalesOrderSummaryDto>>;

internal sealed record SalesOrderSummaryDto(
    long Id, string OrderNumber, long CustomerId, string CustomerName, string Status, decimal Total, long CreatedBy, DateTime CreatedUtc);

internal sealed record SalesOrderSummaryRow(
    long Id, string OrderNumber, long CustomerId, string CustomerName, int Status, decimal Total, long CreatedBy, DateTime CreatedUtc);

internal sealed class SearchSalesOrdersValidator : AbstractValidator<SearchSalesOrdersQuery>
{
    public SearchSalesOrdersValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
        RuleFor(x => x.Status)
            .Must(s => s is null || Enum.TryParse<SalesOrderStatus>(s, ignoreCase: true, out _))
            .WithMessage(x => $"Unknown status '{x.Status}'.");
    }
}

internal sealed class SearchSalesOrdersHandler(IDbConnectionFactory connections, ICurrentUser currentUser)
    : IQueryHandler<SearchSalesOrdersQuery, PagedResult<SalesOrderSummaryDto>>
{
    public async ValueTask<PagedResult<SalesOrderSummaryDto>> Handle(SearchSalesOrdersQuery q, CancellationToken ct)
    {
        int? status = q.Status is null ? null : (int)Enum.Parse<SalesOrderStatus>(q.Status, ignoreCase: true);
        var args = new
        {
            q.CustomerId, Status = status, Search = string.IsNullOrWhiteSpace(q.Search) ? null : q.Search.Trim(),
            CreatedBy = SalesOrder.CreatorFilterFor(new UserId(currentUser.UserId), currentUser.Role)?.Value,
            q.Page, q.PageSize
        };

        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM sales.search_sales_orders(@CustomerId, @Status, @CreatedBy, @Search, @Page, @PageSize);
            SELECT sales.count_sales_orders(@CustomerId, @Status, @CreatedBy, @Search);
            """, args, cancellationToken: ct));

        var items = (await multi.ReadAsync<SalesOrderSummaryRow>())
            .Select(r => new SalesOrderSummaryDto(r.Id, r.OrderNumber, r.CustomerId, r.CustomerName,
                ((SalesOrderStatus)r.Status).ToString(), r.Total, r.CreatedBy, r.CreatedUtc))
            .ToList();
        return new PagedResult<SalesOrderSummaryDto>(items, q.Page, q.PageSize, (int)await multi.ReadSingleAsync<long>());
    }
}
```

Queries go through the validation behavior too. Here it caps the page size, and rejects a status that
doesn't exist with a clear 400 instead of an empty list.

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
            async (long? customerId, string? status, string? search, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(
                    new SearchSalesOrdersQuery(customerId, status, search, page ?? 1, pageSize ?? 20), ct)))
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Sellers));
}
```

That comment points at a real trap. Minimal APIs treat a query parameter declared as plain `int` as
*required*. So `GET /api/sales-orders`, with no `?page=`, would fail with a 400 before your handler ever
ran. Make it `int?` and give it a default yourself.

## 6. Customers and the price list

Two more slices, the same pattern, shorter. When the result columns already match what the caller should
receive, there's no need for a separate `Row` record: Dapper fills the DTO directly.

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Features/SearchCustomers/SearchCustomersHandler.cs
using Dapper;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;

namespace Erp.Modules.Sales.Features;

internal sealed record SearchCustomersQuery(string? Search, int Page, int PageSize) : IQuery<PagedResult<CustomerDto>>;

internal sealed record GetCustomerQuery(long CustomerId) : IQuery<CustomerDto?>;

// Available credit is what the next order may use: the limit, minus what they owe, minus what's awaiting approval.
internal sealed record CustomerDto(
    long Id, string Name, bool IsBlocked, decimal CreditLimit, decimal OutstandingBalance, decimal PendingTotal, decimal AvailableCredit);

internal sealed class SearchCustomersValidator : AbstractValidator<SearchCustomersQuery>
{
    public SearchCustomersValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
    }
}

internal sealed class SearchCustomersHandler(IDbConnectionFactory connections)
    : IQueryHandler<SearchCustomersQuery, PagedResult<CustomerDto>>, IQueryHandler<GetCustomerQuery, CustomerDto?>
{
    public async ValueTask<PagedResult<CustomerDto>> Handle(SearchCustomersQuery q, CancellationToken ct)
    {
        var search = string.IsNullOrWhiteSpace(q.Search) ? null : q.Search.Trim();
        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM sales.search_customers(NULL, @Search, @Page, @PageSize);
            SELECT sales.count_customers(@Search);
            """, new { Search = search, q.Page, q.PageSize }, cancellationToken: ct));

        var rows = (await multi.ReadAsync<CustomerDto>()).ToList();
        return new PagedResult<CustomerDto>(rows, q.Page, q.PageSize, (int)await multi.ReadSingleAsync<long>());
    }

    public async ValueTask<CustomerDto?> Handle(GetCustomerQuery q, CancellationToken ct)
    {
        await using var conn = connections.Create();
        return await conn.QuerySingleOrDefaultAsync<CustomerDto>(new CommandDefinition(
            "SELECT * FROM sales.search_customers(@CustomerId, NULL, 1, 1);", new { q.CustomerId }, cancellationToken: ct));
    }
}
```

One handler class answers two queries, "search" and "get one", because they share one SQL function. That's
still one feature, in one file.

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Features/SearchCustomers/SearchCustomersEndpoint.cs
using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed class SearchCustomersEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/customers", async (string? search, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new SearchCustomersQuery(search, page ?? 1, pageSize ?? 20), ct)))
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Sellers));

        app.MapGet("/customers/{id:long}", async (long id, ISender sender, CancellationToken ct) =>
                await sender.Send(new GetCustomerQuery(id), ct) is { } customer ? Results.Ok(customer) : Results.NotFound())
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Sellers));
    }
}
```

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Features/SearchPriceList/SearchPriceListHandler.cs
using Dapper;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;

namespace Erp.Modules.Sales.Features;

internal sealed record SearchPriceListQuery(string? Search, bool ForSaleOnly, int Page, int PageSize) : IQuery<PagedResult<PriceListItemDto>>;

internal sealed record PriceListItemDto(
    long Id, string Sku, string Name, string Unit, bool AllowsFractions, bool IsActive,
    decimal UnitPrice, long? CategoryId, string? CategoryName, bool IsForSale);

internal sealed class SearchPriceListValidator : AbstractValidator<SearchPriceListQuery>
{
    public SearchPriceListValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
    }
}

internal sealed class SearchPriceListHandler(IDbConnectionFactory connections)
    : IQueryHandler<SearchPriceListQuery, PagedResult<PriceListItemDto>>
{
    public async ValueTask<PagedResult<PriceListItemDto>> Handle(SearchPriceListQuery q, CancellationToken ct)
    {
        var args = new { Search = string.IsNullOrWhiteSpace(q.Search) ? null : q.Search.Trim(), q.ForSaleOnly, q.Page, q.PageSize };
        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM sales.search_price_list(@Search, @ForSaleOnly, @Page, @PageSize);
            SELECT sales.count_price_list(@Search, @ForSaleOnly);
            """, args, cancellationToken: ct));

        var rows = (await multi.ReadAsync<PriceListItemDto>()).ToList();
        return new PagedResult<PriceListItemDto>(rows, q.Page, q.PageSize, (int)await multi.ReadSingleAsync<long>());
    }
}
```

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Features/SearchPriceList/SearchPriceListEndpoint.cs
using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed class SearchPriceListEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet("/price-list", async (string? search, bool? forSaleOnly, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new SearchPriceListQuery(search, forSaleOnly ?? false, page ?? 1, pageSize ?? 20), ct)))
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Sellers));
}
```

## 7. The screens

The browser app can't use the server's DTOs: they're `internal` to the Sales module, and the app must not
reference a module anyway. So it keeps its own copies of the shapes it receives:

```csharp
// src/Erp.Web/Models/Models.cs
namespace Erp.Web.Models;

// The API's JSON shapes, as the browser sees them. The server's own types are internal to each module, so the
// browser app keeps its own copies: the HTTP contract is the only thing the two share.

public sealed record SalesOrderSummary(long Id, string OrderNumber, long CustomerId, string CustomerName, string Status,
    decimal Total, long CreatedBy, DateTime CreatedUtc);

public sealed record SalesOrderLine(long ItemId, string ItemName, string Unit, decimal Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record SalesOrderDetail(long Id, string OrderNumber, long CustomerId, string CustomerName, string Status, decimal Total,
    long CreatedBy, DateTime CreatedUtc, DateTime? SubmittedUtc, long? ApprovedBy, DateTime? ApprovedUtc, long? RejectedBy,
    string? RejectionReason, List<SalesOrderLine> Lines);

public sealed record Customer(long Id, string Name, bool IsBlocked, decimal CreditLimit, decimal OutstandingBalance,
    decimal PendingTotal, decimal AvailableCredit);

public sealed record PriceListItem(long Id, string Sku, string Name, string Unit, bool AllowsFractions, bool IsActive,
    decimal UnitPrice, long? CategoryId, string? CategoryName, bool IsForSale);
```

Add `@using Erp.Web.Models` to `_Imports.razor`. Every later lesson adds its shapes to this file.

Now, let's be honest about the cost: these are copies, and a copy can drift. If the API renames a field,
the app shows a blank where the value used to be. The UI tests in [lesson 12](12-testing.md) catch that,
because they look at real screens.

A few helpers every screen will use:

```csharp
// src/Erp.Web/Shared/Format.cs
using System.Globalization;

namespace Erp.Web.Shared;

public static class Format
{
    private static readonly CultureInfo Ph = CultureInfo.GetCultureInfo("en-PH");

    public static string Peso(decimal amount) => "₱" + amount.ToString("N2", Ph);
    public static string Qty(decimal quantity) => quantity.ToString("0.###", Ph);
    public static string Date(DateTime? utc) => utc is { } d ? d.ToLocalTime().ToString("MMM d, yyyy h:mm tt", Ph) : "";

    // "PendingApproval" -> "Pending approval"
    public static string Words(string pascal) =>
        string.Concat(pascal.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLower(c) : c.ToString()));
}
```

```csharp
// src/Erp.Web/Shared/Url.cs
namespace Erp.Web.Shared;

public static class Url
{
    // api/sales-orders?status=Approved&search=acme, leaving out anything empty: the API would reject "status=".
    public static string With(string path, params (string Name, object? Value)[] query)
    {
        var parts = query
            .Where(q => q.Value is not null && q.Value.ToString() is { Length: > 0 })
            .Select(q => $"{q.Name}={Uri.EscapeDataString(Convert.ToString(q.Value, System.Globalization.CultureInfo.InvariantCulture)!)}");
        var queryString = string.Join("&", parts);
        return queryString.Length == 0 ? path : $"{path}?{queryString}";
    }
}
```

`InvariantCulture` matters more than it looks: on a computer set to a language that writes 2,5 for two and
a half, a quantity would otherwise reach the API with a comma.

```razor
@* src/Erp.Web/Shared/StatusChip.razor *@
@* One look for a status everywhere: green when done, amber when someone must act, grey when over. *@
<MudChip T="string" Size="Size.Small" Color="ColorOf(Status)" Variant="Variant.Text">@Format.Words(Status)</MudChip>

@code {
    [Parameter, EditorRequired] public string Status { get; set; } = "";

    private static Color ColorOf(string status) => status switch
    {
        "Approved" => Color.Success,
        "PendingApproval" => Color.Warning,
        "Rejected" => Color.Error,
        "Cancelled" => Color.Default,
        _ => Color.Info
    };
}
```

Later modules add their own statuses to that `switch`. And amounts line up best right-aligned in equal-width
digits, so add this to `app.css`:

```css
.money {
    text-align: right;
    font-variant-numeric: tabular-nums;
    white-space: nowrap;
}
```

`ApiClient` learns to fetch one page for a table. In `Services/ApiClient.cs`, add the envelope above the
class, and one method inside it:

```csharp
public sealed record PagedResult<T>(List<T> Items, int Page, int PageSize, int TotalCount);
```

```csharp
// One page of a list for a MudTable: the table counts pages from 0, the API from 1.
public async Task<TableData<T>> Page<T>(string url, TableState state)
{
    var separator = url.Contains('?') ? '&' : '?';
    var result = await Get<PagedResult<T>>($"{url}{separator}page={state.Page + 1}&pageSize={state.PageSize}");
    return new TableData<T> { Items = result?.Items ?? [], TotalItems = result?.TotalCount ?? 0 };
}
```

### The order list

```razor
@* src/Erp.Web/Pages/Sales/Orders.razor *@
@page "/sales-orders"
@inject ApiClient Api
@inject NavigationManager Nav

<PageTitle>Sales orders · ERP</PageTitle>

<MudStack Row="true" AlignItems="AlignItems.Center" Class="mb-4">
    <MudText Typo="Typo.h4">Sales orders</MudText>
    <MudSpacer />
    <AuthorizeView Roles="@Sellers">
        <MudButton Href="sales-orders/new" Variant="Variant.Filled" Color="Color.Primary" StartIcon="@Icons.Material.Filled.Add">New order</MudButton>
    </AuthorizeView>
</MudStack>

<MudPaper Class="pa-4 mb-4" Elevation="0" Outlined="true">
    <MudStack Row="true" Spacing="4">
        <MudTextField T="string" Value="_search" ValueChanged="OnSearch" Label="Order number or customer" DebounceInterval="400"
                      Adornment="Adornment.Start" AdornmentIcon="@Icons.Material.Filled.Search" Clearable="true" />
        <MudSelect T="string" Value="Status" ValueChanged="OnStatus" Label="Status" Clearable="true" Style="max-width:240px">
            @foreach (var s in Statuses)
            {
                <MudSelectItem Value="s">@Format.Words(s)</MudSelectItem>
            }
        </MudSelect>
    </MudStack>
</MudPaper>

<MudTable T="SalesOrderSummary" @ref="_table" ServerData="Load" Hover="true" Dense="true"
          OnRowClick="Open" RowStyle="cursor:pointer">
    <HeaderContent>
        <MudTh>Order</MudTh>
        <MudTh>Customer</MudTh>
        <MudTh>Status</MudTh>
        <MudTh Class="money">Total</MudTh>
        <MudTh>Created</MudTh>
    </HeaderContent>
    <RowTemplate>
        <MudTd>@context.OrderNumber</MudTd>
        <MudTd>@context.CustomerName</MudTd>
        <MudTd><StatusChip Status="@context.Status" /></MudTd>
        <MudTd Class="money">@Format.Peso(context.Total)</MudTd>
        <MudTd>@Format.Date(context.CreatedUtc)</MudTd>
    </RowTemplate>
    <NoRecordsContent>No orders match.</NoRecordsContent>
    <PagerContent><MudTablePager /></PagerContent>
</MudTable>

@code {
    [SupplyParameterFromQuery] public string? Status { get; set; }

    private static readonly string[] Statuses = ["Draft", "PendingApproval", "Approved", "Rejected", "Cancelled"];
    private static readonly string Sellers = $"{Roles.SalesRep},{Roles.SalesManager},{Roles.Admin}";
    private MudTable<SalesOrderSummary> _table = default!;
    private string? _search;

    private Task<TableData<SalesOrderSummary>> Load(TableState state, CancellationToken ct) =>
        Api.Page<SalesOrderSummary>(Url.With("api/sales-orders", ("status", Status), ("search", _search)), state);

    private void Open(TableRowClickEventArgs<SalesOrderSummary> row) => Nav.NavigateTo($"sales-orders/{row.Item!.Id}");

    private Task OnSearch(string? text) { _search = text; return _table.ReloadServerData(); }
    private Task OnStatus(string? status) { Status = status; return _table.ReloadServerData(); }
}
```

- **No role attribute on the page.** Any signed-in user may open it; the API answers 403 to anyone it
  shouldn't serve, and `ApiClient` says so. The **New order** button, though, only shows to people who may
  sell.
- **`ServerData="Load"`** makes the table ask the server for each page, instead of downloading every order
  and paging in the browser. With 50,000 orders, that's the difference between instant and unusable.
- **`DebounceInterval="400"`** waits until you stop typing for 0.4 seconds before searching, so typing
  "acme" sends one request, not four.
- **`[SupplyParameterFromQuery] Status`** reads `?status=PendingApproval` from the address, so a link can
  open the list already filtered. The home page will use that.

### One order

```razor
@* src/Erp.Web/Pages/Sales/OrderDetail.razor *@
@page "/sales-orders/{Id:long}"
@inject ApiClient Api
@inject NavigationManager Nav

<PageTitle>@(_order?.OrderNumber ?? "Order") · ERP</PageTitle>

@if (_order is null)
{
    <MudProgressLinear Indeterminate="true" />
    return;
}

<MudStack Row="true" AlignItems="AlignItems.Center" Class="mb-4" Wrap="Wrap.Wrap">
    <MudText Typo="Typo.h4">@_order.OrderNumber</MudText>
    <StatusChip Status="@_order.Status" />
    <MudSpacer />
</MudStack>

<MudGrid>
    <MudItem xs="12" md="8">
        <MudPaper Class="pa-4" Outlined="true">
            <MudText Typo="Typo.h6" GutterBottom="true">@_order.CustomerName</MudText>
            <MudSimpleTable Dense="true" Hover="true">
                <thead><tr><th>Item</th><th class="money">Quantity</th><th class="money">Unit price</th><th class="money">Amount</th></tr></thead>
                <tbody>
                    @foreach (var line in _order.Lines)
                    {
                        <tr>
                            <td>@line.ItemName</td>
                            <td class="money">@Format.Qty(line.Quantity) @line.Unit</td>
                            <td class="money">@Format.Peso(line.UnitPrice)</td>
                            <td class="money">@Format.Peso(line.LineTotal)</td>
                        </tr>
                    }
                    <tr><td colspan="3"><b>Total</b></td><td class="money"><b>@Format.Peso(_order.Total)</b></td></tr>
                </tbody>
            </MudSimpleTable>
        </MudPaper>
    </MudItem>
    <MudItem xs="12" md="4">
        <MudPaper Class="pa-4" Outlined="true">
            <MudText Typo="Typo.h6" GutterBottom="true">History</MudText>
            <MudText Typo="Typo.body2">Created @Format.Date(_order.CreatedUtc)</MudText>
        </MudPaper>
    </MudItem>
</MudGrid>

@code {
    [Parameter] public long Id { get; set; }

    private SalesOrderDetail? _order;

    protected override Task OnParametersSetAsync() => Load();

    private async Task Load()
    {
        _order = await Api.Get<SalesOrderDetail>($"api/sales-orders/{Id}");
        if (_order is null) Nav.NavigateTo("sales-orders");
    }
}
```

For now the page only shows the order. The next three lessons add a button each (Submit, then Approve,
Reject and Cancel, then Edit) and the history grows a line each time. The `<MudSpacer />` in the header is
where those buttons will go.

`return;` inside the markup stops rendering there, so the rest of the page never touches a `null` order.

### The form, with search boxes

Here's the order form from lesson 06, refactored. The id boxes become **autocompletes**: you type part of a
name, and it searches as you type.

Before:

```razor
<MudNumericField T="long" Label="Customer id" @bind-Value="_customerId" Min="1" Style="max-width:200px" />
```

After, the whole page:

```razor
@* src/Erp.Web/Pages/Sales/OrderEditor.razor *@
@page "/sales-orders/new"
@attribute [Authorize(Roles = $"{Roles.SalesRep},{Roles.SalesManager},{Roles.Admin}")]
@inject ApiClient Api
@inject NavigationManager Nav

<PageTitle>New order · ERP</PageTitle>

<MudText Typo="Typo.h4" Class="mb-4">New sales order</MudText>

<MudPaper Class="pa-4 mb-4" Outlined="true">
    <MudAutocomplete T="Customer" Label="Customer" @bind-Value="_customer" SearchFunc="FindCustomers" ToStringFunc="c => c?.Name"
                     ResetValueOnEmptyText="true" DebounceInterval="300" />
    @if (_customer is not null)
    {
        <MudText Typo="Typo.body2" Class="mt-2">
            Available credit: <b>@Format.Peso(_customer.AvailableCredit)</b>
            @if (_customer.IsBlocked) { <MudChip T="string" Color="Color.Error" Size="Size.Small">Blocked</MudChip> }
        </MudText>
    }
</MudPaper>

<MudPaper Class="pa-4 mb-4" Outlined="true">
    <MudText Typo="Typo.h6" GutterBottom="true">Lines</MudText>
    @foreach (var line in _lines)
    {
        <MudStack Row="true" AlignItems="AlignItems.Center" Spacing="3" Class="mb-2">
            <MudAutocomplete T="PriceListItem" Label="Item" @bind-Value="line.Item" SearchFunc="FindItems" Style="min-width:320px"
                             ToStringFunc="@(i => i is null ? null : $"{i.Sku} · {i.Name}")" DebounceInterval="300" />
            <MudNumericField T="decimal" Label="@($"Quantity{(line.Item is null ? "" : $" ({line.Item.Unit})")}")" @bind-Value="line.Quantity"
                             Min="0" Step="@(line.Item?.AllowsFractions == true ? 0.5m : 1m)" Style="max-width:160px" />
            <MudText Class="money" Style="min-width:140px">@Format.Peso(line.Total)</MudText>
            <MudIconButton Icon="@Icons.Material.Filled.Delete" OnClick="() => _lines.Remove(line)" aria-label="Remove line" />
        </MudStack>
    }
    <MudButton OnClick="() => _lines.Add(new Line())" StartIcon="@Icons.Material.Filled.Add" Variant="Variant.Text">Add line</MudButton>
    <MudDivider Class="my-3" />
    <MudText Typo="Typo.h6" Align="Align.Right">Total @Format.Peso(_lines.Sum(l => l.Total))</MudText>
    @if (_lines.Sum(l => l.Total) > 50_000)
    {
        <MudText Typo="Typo.body2" Align="Align.Right" Color="Color.Warning">Over ₱50,000: a manager approves it after you submit.</MudText>
    }
</MudPaper>

<MudStack Row="true" Spacing="2">
    <MudButton OnClick="Save" Variant="Variant.Filled" Color="Color.Primary" Disabled="!CanSave">Save draft</MudButton>
    <MudButton Href="sales-orders" Variant="Variant.Text">Back</MudButton>
</MudStack>

@code {
    private sealed class Line
    {
        public PriceListItem? Item { get; set; }
        public decimal Quantity { get; set; } = 1;
        public decimal Total => Item is null ? 0 : Math.Round(Item.UnitPrice * Quantity, 2, MidpointRounding.AwayFromZero);
    }

    private Customer? _customer;
    private List<Line> _lines = [new()];

    private bool CanSave => _customer is not null && _lines.Count > 0 && _lines.All(l => l.Item is not null && l.Quantity > 0);

    private async Task<IEnumerable<Customer>> FindCustomers(string? text, CancellationToken ct) =>
        (await Api.Get<PagedResult<Customer>>(Url.With("api/customers", ("search", text), ("pageSize", 20))))?.Items ?? [];

    private async Task<IEnumerable<PriceListItem>> FindItems(string? text, CancellationToken ct) =>
        (await Api.Get<PagedResult<PriceListItem>>(Url.With("api/price-list", ("search", text), ("forSaleOnly", true), ("pageSize", 20))))?.Items ?? [];

    private async Task Save()
    {
        var lines = _lines.Select(l => new { itemId = l.Item!.Id, quantity = l.Quantity }).ToList();
        if (await Api.Create("api/sales-orders", new { customerId = _customer!.Id, lines }, "Draft saved.") is { } id)
            Nav.NavigateTo($"sales-orders/{id}");
    }
}
```

The `ISnackbar` we injected last time is gone: `ApiClient.Create` shows "Draft saved." itself, and the
page goes straight to the new order.

The form shows the total and the "a manager approves it" hint as you type. Both are computed in the
browser, from the same prices, with the same rounding, but they're only a preview: the domain decides
again on the server. If the two ever disagree, the server wins, and the user sees its message.

### The menu and the home page

In `NavMenu.razor`, the Sales group now opens the list (the list has the **New order** button):

```razor
<MudNavLink Href="sales-orders">Sales orders</MudNavLink>
```

And give the home page its first shortcut. A tile is a small card with a link:

```razor
@* src/Erp.Web/Shared/Tile.razor *@
@attribute [AllowAnonymous]

<MudCard Elevation="1" Style="height:100%">
    <MudCardContent>
        <MudStack Row="true" AlignItems="AlignItems.Center" Spacing="2">
            <MudIcon Icon="@Icon" Color="Color.Primary" />
            <MudText Typo="Typo.h6">@Title</MudText>
        </MudStack>
        <MudText Typo="Typo.body2" Class="mt-2">@Text</MudText>
    </MudCardContent>
    <MudCardActions>
        <MudButton Href="@Href" Color="Color.Primary" Variant="Variant.Text">Open</MudButton>
    </MudCardActions>
</MudCard>

@code {
    [Parameter] public string Title { get; set; } = "";
    [Parameter] public string Text { get; set; } = "";
    [Parameter] public string Href { get; set; } = "";
    [Parameter] public string Icon { get; set; } = "";
}
```

`[Parameter]` makes a property settable from the outside, like an attribute on an HTML tag. In `Home.razor`,
under the subtitle and still inside `<AuthorizeView>`:

```razor
<MudGrid>
    <AuthorizeView Roles="@SellerRoles">
        <MudItem xs="12" sm="6" md="4">
            <Tile Title="New sales order" Text="Pick a customer, add lines, submit." Href="sales-orders/new" Icon="@Icons.Material.Filled.AddShoppingCart" />
        </MudItem>
    </AuthorizeView>
</MudGrid>

@code {
    private static readonly string SellerRoles = $"{Roles.SalesRep},{Roles.SalesManager},{Roles.Admin}";
}
```

## How a request flows

```mermaid
sequenceDiagram
  participant B as Browser (Orders page)
  participant H as SearchSalesOrdersHandler
  participant D as SalesOrder.CreatorFilterFor
  participant PG as PostgreSQL
  B->>H: GET /api/sales-orders?search=acme&page=1&pageSize=10
  H->>D: who may this person see?
  D-->>H: rep → own id; manager → null
  H->>PG: search_sales_orders(..., @CreatedBy, 'acme', 1, 10); count_sales_orders(...)
  PG-->>H: 10 rows + total
  H-->>B: { items, page, pageSize, totalCount, totalPages }
```

No change tracking, no transaction, one trip to the database. Easy, right?

## Mistakes to avoid

- Loading aggregates with EF Core to fill a list screen. Use a read query shaped for the screen.
- Pasting values into SQL strings. Always use parameters (`@OrderId`).
- Editing a released `.sql` file. Add `.v2` and a new migration.
- Filtering "my orders" in the browser. The browser can be bypassed; the filter belongs in the query.
- Answering 403 for someone else's order. 404 tells them nothing.
- Declaring an optional query parameter as plain `int`.
- Paging in the browser. `ServerData` asks for one page at a time.

## Let's run it!

Restart the app so the new migration runs. Then, as a rep and as a manager:

```bash
REP=$(curl -s -X POST http://localhost:5264/api/auth/login -H "Content-Type: application/json" \
  -d '{"email":"rep@erp.local","password":"Password123!"}' | jq -r .accessToken)
MGR=$(curl -s -X POST http://localhost:5264/api/auth/login -H "Content-Type: application/json" \
  -d '{"email":"manager@erp.local","password":"Password123!"}' | jq -r .accessToken)

curl -s "http://localhost:5264/api/sales-orders?search=acme&pageSize=5" -H "Authorization: Bearer $REP" | jq '.totalCount'
curl -s "http://localhost:5264/api/sales-orders?page=9999" -H "Authorization: Bearer $REP" | jq '{items, totalCount}'
# { "items": [], "totalCount": 3 }   (your count will differ, but it's not 0)
curl -s "http://localhost:5264/api/sales-orders?status=Shipped" -H "Authorization: Bearer $REP" | jq .errors
# [ "Unknown status 'Shipped'." ]   (for now: lesson 17 adds it)
curl -s "http://localhost:5264/api/customers?search=beta" -H "Authorization: Bearer $REP" | jq '.items[0].availableCredit'
# 500.0
```

Now the visibility rule. Create an order as the manager, then try to read it as the rep:

```bash
ID=$(curl -s -X POST http://localhost:5264/api/sales-orders -H "Authorization: Bearer $MGR" \
  -H "Content-Type: application/json" -d '{"customerId":1,"lines":[{"itemId":1,"quantity":1}]}' | jq .id)
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5264/api/sales-orders/$ID -H "Authorization: Bearer $MGR"   # 200
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5264/api/sales-orders/$ID -H "Authorization: Bearer $REP"   # 404
```

And in the app: sign in as the rep, open **Sales orders**, type `acme` in the search box, and click an order.
Then **New order**: type `be` in the customer box, pick Beta Industries, and see "Available credit:
₱500.00". Add 30 Widgets and watch the total reach ₱750.00. Save it, and the red snackbar says it would
exceed the credit limit. Now pick Copper Wire, and the quantity box steps by 0.5 kg.

You can also call a function straight from `psql`, which is handy when you're tuning one:

```bash
PGPASSWORD=erp_dev_only "/c/Program Files/PostgreSQL/18/bin/psql.exe" -h localhost -U postgres -d erp \
  -c "SELECT * FROM sales.search_sales_orders(NULL, NULL, NULL, NULL, 1, 5);"
```

## Let's check!

1. Why don't the query handlers load `SalesOrder` aggregates with EF Core?
2. Why are the reads PostgreSQL *functions* rather than stored procedures?
3. A function needs a new column. What files do you add, and what must you never do?
4. A rep asks for a manager's order by id. What does the API answer, and why that status?
5. Where is "a rep sees only their own orders" decided, and how do the list and the single-order read both
   use it?
6. What happens on `GET /api/sales-orders` if `page` is declared as `int` instead of `int?`?
7. Why does the browser app keep its own copies of the API's shapes?

And that's where this lesson ends. Every write goes through the domain, every read goes straight to a query
built for its screen, and a rep sees only their own pipeline. Next time, we submit an order: reserving stock
in a second module, inside one transaction. Good luck, developer!

---

← [Lesson 06 — Create an order](06-create-order.md) · Next: [Lesson 08 — Submit](08-submit.md) →
