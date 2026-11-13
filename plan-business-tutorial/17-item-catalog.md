# Lesson 17 — Item Catalog and Stock: One Owner, Copies Kept in Step

Good day, developer! Until today, the same five items lived in two seed scripts, Sales' and Inventory's, and
nothing kept them in step. Add a sixth item and you'd have to add it twice, and hope. Today the warehouse
gets the item catalog for real: the clerk adds an item once, in Inventory, and Sales' copy appears by
itself a moment later, through an event. The clerk also gets stock adjustments with a full audit trail, and
the sales manager gets a price list. About two and a half hours. Are you ready? Let's begin!

## What we'll build

| Endpoint | Who | What it does |
|---|---|---|
| `GET /api/items` | Anyone signed in | Items with on-hand, reserved and available stock |
| `POST /api/items`, `PUT /api/items/{id}` | InventoryClerk, Admin | Add an item; rename it or change its unit |
| `POST /api/items/{id}/activate` and `/deactivate` | InventoryClerk, Admin | Stop or resume using an item |
| `POST /api/items/{id}/adjustments` | InventoryClerk, Admin | Correct the count: found, lost, damaged, with a reason |
| `GET /api/items/{id}/movements` | Anyone signed in | Every change to an item's stock, who made it and why |
| `PUT /api/price-list/{itemId}` | SalesManager, Admin | Price, category, and whether it's for sale |
| `GET`/`POST`/`PUT /api/item-categories` | Read: Sales readers; write: managers | The category rules from lesson 04 |

Plus the first **integration event**, `ItemCatalogChanged`, which travels from Inventory to Sales through the
outbox, and three screens: Items and stock, Price list, and Categories.

## Why we build it this way

### One owner per fact

Who decides what an item *is*? Its SKU, its name, its unit, whether it's still in use? The warehouse does:
they receive it, count it and ship it. Who decides what it *costs* a customer, and who may sell it? Sales
does. So:

| Fact | Owner |
|---|---|
| SKU, name, unit, fractions allowed, active | Inventory |
| On hand, reserved | Inventory |
| Price, category, for sale | Sales |

Each fact has exactly one module that may change it.

### But Sales needs the name, every time

Sales prints the item's name on every order line and checks its unit on every quantity. Asking Inventory
through a contract for each of those would tie every Sales screen to Inventory being up and quick. And a
`JOIN` into `inventory.item` is forbidden: no SQL crosses a schema.

So Sales keeps its own **copy** of the few catalog facts it needs. You built that copy in lesson 04: it's
`SalesItem`, with `FromCatalog` and `ApplyCatalog` waiting for this day.

### Keeping the copy in step

When the clerk adds or renames an item, Inventory raises an event, `ItemCatalogChanged`, carrying the whole
catalog record. Sales handles it and writes the same facts into its copy. Lesson 13's outbox delivers it,
so a crash can't lose it.

Now, let's be honest about the cost: the copy is **eventually consistent**. For a second or two after the
clerk saves, Sales still has the old name. For an item's name and unit, that's fine. It would *not* be fine
for stock levels, which is why stock stays in Inventory and is reserved through a contract, inside the
order's transaction, exactly as in lesson 09.

Here's the difference between the two kinds of message:

| | Domain event (lesson 13) | Integration event (today) |
|---|---|---|
| Who handles it | The same module | Other modules |
| Lives in | The module, `internal` | The module's **Contracts** project, `public` |
| Carries | Typed ids, value objects | Plain types: `long`, `string`, `bool` |

An integration event is part of the module's service window, so it's written like the contract interfaces:
plain types, no domain objects from another island.

### The event must carry the id

The outbox writes events *during* `SaveChanges`. But an identity column only gets its value *after* the
`INSERT`. An event raised in `Item.Create` would carry id 0. So Inventory takes the id from a **sequence**
first, before creating the item, like lesson 07 did for order numbers.

## 1. Inventory's item becomes the catalog

Here's lesson 09's item, before:

```csharp
// Same id as Sales' item: the two modules talk about one item, each keeping only what it needs.
internal sealed class Item : Entity<ItemId>
{
    public decimal OnHand { get; private set; }
    public decimal Reserved { get; private set; }   // promised to submitted sales orders
    public decimal Available => OnHand - Reserved;

    private Item() { }
}
```

And after, the whole catalog:

```csharp
// src/Modules/Inventory/Erp.Modules.Inventory/Domain/Item.cs
using Erp.Modules.Inventory.Contracts;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Inventory.Domain;

internal readonly record struct ItemId(long Value);
internal readonly record struct UserId(long Value);

// The item catalog and its stock. Other modules keep their own copy of what they need (Sales: name and unit),
// kept in step by ItemCatalogChanged.
internal sealed class Item : AggregateRoot<ItemId>
{
    public string Sku { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string Unit { get; private set; } = default!;
    public bool AllowsFractions { get; private set; }
    public bool IsActive { get; private set; }
    public decimal OnHand { get; private set; }
    public decimal Reserved { get; private set; }   // promised to submitted sales orders
    public decimal Available => OnHand - Reserved;

    private Item() { }

    public static Item Create(ItemId id, string sku, string name, string unit, bool allowsFractions)
    {
        var item = new Item { Id = id, Sku = Required(sku, "SKU").ToUpperInvariant(), IsActive = true };
        item.Describe(name, unit, allowsFractions);
        return item;
    }

    public void Describe(string name, string unit, bool allowsFractions)
    {
        Name = Required(name, "Name");
        Unit = Required(unit, "Unit").ToLowerInvariant();
        AllowsFractions = allowsFractions;
        RaiseChanged();
    }

    public void SetActive(bool active)
    {
        if (!active && Reserved > 0)
            throw new DomainException(InventoryErrors.ItemHasReservations, $"Item {Sku} has {Reserved:0.###} {Unit} reserved for orders and can't be deactivated.");
        IsActive = active;
        RaiseChanged();
    }

    public void EnsureQuantityFits(decimal quantity)
    {
        if (!AllowsFractions && decimal.Truncate(quantity) != quantity)
            throw new DomainException(InventoryErrors.WholeUnitsOnly, $"Item {Sku} is counted in whole {Unit}; {quantity:0.###} is not allowed.");
    }

    // A count correction: found, lost, damaged. Never below what's already promised to orders.
    public StockMovement Adjust(decimal delta, string reason, UserId by, DateTime nowUtc)
    {
        // Every check first: a refused adjustment must leave the item exactly as it was.
        if (delta == 0) throw new DomainException(InventoryErrors.AdjustmentMustChangeQuantity, "An adjustment must change the quantity.");
        var why = Required(reason, "Reason");
        EnsureQuantityFits(Math.Abs(delta));
        if (OnHand + delta < Reserved)
            throw new DomainException(InventoryErrors.AdjustmentBelowReserved,
                $"Adjusting {Sku} by {delta:0.###} would leave {OnHand + delta:0.###} {Unit} on hand, " +
                $"less than the {Reserved:0.###} reserved for orders.");
        OnHand += delta;
        return StockMovement.Adjustment(Id, delta, why, by, nowUtc);
    }

    private void RaiseChanged() =>
        Raise(new ItemCatalogChanged(Id.Value, Sku, Name, Unit, AllowsFractions, IsActive));

    private static string Required(string value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new DomainException(InventoryErrors.FieldRequired, $"{field} is required.") : value.Trim();
}
```

What each rule protects:

- **SKUs are upper case, units lower case.** `gen-001` and `GEN-001` are one item, and `PC` and `pc` are one
  unit, however they were typed.
- **The SKU never changes after creation.** It's printed on labels, purchase orders and invoices. There's
  no method that sets it.
- **An item with stock reserved for orders can't be deactivated.** Those orders still have to ship.
- **An adjustment can't leave less on hand than is reserved**, which also means it can never go below zero.
- **"Every check first."** `Adjust` checks everything before it changes anything. An early version changed
  `OnHand` and *then* checked the reason; a refused adjustment left the count wrong in memory. A test caught
  it, and that's why the comment is there.
- **Every change that Sales' copy cares about calls `RaiseChanged`**, with the full record, never just
  the changed field. A handler that receives the full state can apply it twice, or out of order, and end up
  right.

Notice `Item` raises an event, so it's an `AggregateRoot` now, and `Adjust` returns a `StockMovement`: the
audit row the handler saves next to the item.

```csharp
// src/Modules/Inventory/Erp.Modules.Inventory/Domain/StockMovement.cs
namespace Erp.Modules.Inventory.Domain;

internal enum MovementKind { Adjustment = 2 }

// The audit trail: every change to on-hand stock, who made it and why. Written once, never changed.
internal sealed class StockMovement
{
    public long Id { get; private set; }
    public ItemId ItemId { get; private set; }
    public decimal Quantity { get; private set; }   // + in, - out
    public MovementKind Kind { get; private set; }
    public string? Reference { get; private set; }   // PO-..., SH-...
    public string? Reason { get; private set; }
    public UserId UserId { get; private set; }
    public DateTime OccurredUtc { get; private set; }

    private StockMovement() { }

    public static StockMovement Adjustment(ItemId item, decimal delta, string reason, UserId by, DateTime nowUtc) =>
        new() { ItemId = item, Quantity = delta, Kind = MovementKind.Adjustment, Reason = reason, UserId = by, OccurredUtc = nowUtc };
}
```

Why does the only kind start at 2? Because two more are coming: receiving a purchase order
([lesson 19](19-purchasing.md)) will be 1, and shipping ([lesson 18](18-shipping.md)) will be 3. The number
is what's stored, so it's decided once and never renumbered.

## 2. The integration event, in the Contracts

```csharp
// add to src/Modules/Inventory/Erp.Modules.Inventory.Contracts/InventoryContracts.cs  (and `using Erp.Shared.BuildingBlocks;`)

// Integration events: public, because other modules handle them. Raised through the Inventory outbox.
public sealed record ItemCatalogChanged(long ItemId, string Sku, string Name, string Unit, bool AllowsFractions, bool IsActive)
    : IDomainEvent;
```

`IDomainEvent` lives in BuildingBlocks, so the Contracts project needs its first reference:

```bash
dotnet add src/Modules/Inventory/Erp.Modules.Inventory.Contracts reference src/Shared/Erp.Shared.BuildingBlocks
```

That's the one reference a Contracts project may have. It still references no module, so no cycle can form.
The architecture test from [lesson 09](09-submit.md) checks exactly that.

## 3. Inventory's database

In `InventoryDbContext`:

```csharp
public DbSet<Item> Items => Set<Item>();
public DbSet<StockMovement> StockMovements => Set<StockMovement>();

// Ids that an event carries must exist before SaveChanges writes the event, so they come from a sequence.
public Task<long> NextItemIdAsync(CancellationToken ct) => NextAsync($"SELECT nextval('inventory.item_id_seq') AS \"Value\"", ct);

// ToList, not Single(): EF would wrap Single() in a subquery.
private async Task<long> NextAsync(FormattableString sql, CancellationToken ct) =>
    (await Database.SqlQuery<long>(sql).ToListAsync(ct)).Single();
```

`NextAsync` takes a `FormattableString` on purpose. EF's `SqlQuery` accepts only interpolated strings, which
it turns into parameters, so there's no way to paste a value into SQL by accident here.

`ConfigureConventions` gains `c.Properties<UserId>().HaveConversion<UserIdConverter>();` (with a
`UserIdConverter` next to `ItemIdConverter`), and `OnModelCreating` becomes:

```csharp
b.HasDefaultSchema(Schema);
b.HasSequence<long>("item_id_seq");

// (the outbox mapping from lesson 13 stays here)

b.Entity<Item>(e =>
{
    e.ToTable("item", t =>
    {
        t.HasCheckConstraint("ck_item_on_hand", "on_hand >= 0");
        t.HasCheckConstraint("ck_item_reserved", "reserved >= 0");
        // Overselling is impossible at the data level, whatever the code does.
        t.HasCheckConstraint("ck_item_not_over", "reserved <= on_hand");
    });
    e.HasKey(x => x.Id);
    e.Property(x => x.Id).ValueGeneratedNever();
    e.HasIndex(x => x.Sku).IsUnique();
    e.Property(x => x.Sku).HasMaxLength(50);
    e.Property(x => x.Name).HasMaxLength(200);
    e.Property(x => x.Unit).HasMaxLength(10);
    e.Property<uint>("RowVersion").IsRowVersion().HasColumnName("xmin");
    e.Ignore(x => x.Available);
    e.Ignore(x => x.DomainEvents);
});

b.Entity<StockMovement>(e =>
{
    e.ToTable("stock_movement");
    e.HasKey(x => x.Id);
    e.Property(x => x.Id).UseIdentityColumn();
    e.Property(x => x.Reference).HasMaxLength(50);
    e.Property(x => x.Reason).HasMaxLength(500);
    e.HasIndex(x => new { x.ItemId, x.OccurredUtc });
    e.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
});
```

The `xmin` row version earns its place here. A clerk opens an item showing 10 on hand, 0 reserved. Meanwhile
a rep submits an order and reserves 8. The clerk adjusts by −5, which looked fine on screen. Without a version
check, EF would write `on_hand = 5` with 8 reserved, and the check constraint would at least refuse it with a
500. With the version, the clerk's save gets a clean 409, reloads, and sees the truth. The stock movement's
foreign key to `item` is fine: both tables are in the `inventory` schema.

The development seed now owns the catalog:

```sql
-- src/Modules/Inventory/Erp.Modules.Inventory/Infrastructure/Sql/DevSeed.sql
-- Development and test data; never runs in production. Item ids match the Sales dev seed.
-- Item 3 has thin stock so the reservation race is testable. Item 5 is sold by the kilogram.
INSERT INTO inventory.item (id, sku, name, unit, allows_fractions, is_active, on_hand, reserved)
SELECT v.id, v.sku, v.name, v.unit, v.allows_fractions, TRUE, v.on_hand, 0
FROM (VALUES (1, 'GEN-001', 'Widget',         'pc', FALSE, 100),
             (2, 'GEN-002', 'Gadget',         'pc', FALSE,  50),
             (3, 'CTL-001', 'Restricted Kit', 'pc', FALSE,   8),
             (4, 'GEN-003', 'Server Rack',    'pc', FALSE, 100),
             (5, 'BLK-001', 'Copper Wire',    'kg', TRUE,  250))
     AS v(id, sku, name, unit, allows_fractions, on_hand)
WHERE NOT EXISTS (SELECT 1 FROM inventory.item);

-- Explicit ids don't advance the sequence; move it past them.
SELECT setval('inventory.item_id_seq', GREATEST((SELECT MAX(id) FROM inventory.item), 1));
```

That last line matters. The seed inserts ids 1 to 5 by hand, so the sequence still thinks the next id is 1.
Without `setval`, the clerk's first new item would collide with the Widget.

Sales' seed keeps its five items, with the comment it has had since lesson 05: "In real use these rows
arrive through ItemCatalogChanged." From today, that's literally true for every new item.

Create the migration:

```bash
dotnet ef migrations add AddItemCatalog \
  --project src/Modules/Inventory/Erp.Modules.Inventory --startup-project src/Modules/Inventory/Erp.Modules.Inventory \
  --output-dir Infrastructure/Migrations
```

One thing to know before you run it: your development database already has five stock rows with no SKU. A
unique index can't be built over five empty SKUs, so the migration fails on that database. It's development
data, so drop the `erp` database and let the app rebuild and reseed it. (A real customer's database never
had this table without SKUs, because this is the version you sell.)

## 4. The reservation refuses inactive items

In `StockService.TryReserveAsync`, the guard in the `WHERE` gains one condition. Before:

```csharp
.Where(i => i.Id == id && i.OnHand - i.Reserved >= quantity)
```

After:

```csharp
.Where(i => i.Id == id && i.IsActive && i.OnHand - i.Reserved >= quantity)
```

Sales already refuses an inactive item through its copy. But the copy is a second or two behind, and this
check is not: it's in the same statement that reserves.

## 5. Maintaining the catalog

Inventory gets endpoints for the first time, so `InventoryModule` learns lesson 03's two registrations. Before:

```csharp
services.AddScoped<IModuleDatabase, InventoryDatabase>();
services.AddScoped<IStockReservation, StockService>();
return services;
```

After:

```csharp
var assembly = typeof(InventoryModule).Assembly;
// ...
services.AddScoped<IModuleDatabase, InventoryDatabase>();
services.AddScoped<IStockReservation, StockService>();

// AddCarter() only finds public endpoint classes; ours are internal.
foreach (var endpoint in assembly.GetTypes().Where(t => !t.IsAbstract && typeof(ICarterModule).IsAssignableFrom(t)))
    services.AddSingleton(typeof(ICarterModule), endpoint);

services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
return services;
```

```bash
dotnet add src/Modules/Inventory/Erp.Modules.Inventory package Carter --version 10.0.0
dotnet add src/Modules/Inventory/Erp.Modules.Inventory package FluentValidation.DependencyInjectionExtensions --version 12.1.1
dotnet add src/Modules/Inventory/Erp.Modules.Inventory package Dapper --version 2.1.79
```

```csharp
// src/Modules/Inventory/Erp.Modules.Inventory/Features/CreateItem/CreateItemHandler.cs
using Erp.Modules.Inventory.Domain;
using Erp.Modules.Inventory.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Inventory.Features;

internal sealed record CreateItemCommand(string Sku, string Name, string Unit, bool AllowsFractions);

internal sealed class CreateItemHandler(InventoryDbContext db)
{
    public async Task<long> HandleAsync(CreateItemCommand cmd, CancellationToken ct)
    {
        var sku = cmd.Sku.Trim().ToUpperInvariant();
        if (await db.Items.AnyAsync(i => i.Sku == sku, ct))
            throw new DomainException(InventoryErrors.SkuTaken, $"An item with SKU {sku} already exists.");

        var id = new ItemId(await db.NextItemIdAsync(ct));
        db.Items.Add(Item.Create(id, sku, cmd.Name, cmd.Unit, cmd.AllowsFractions));
        await db.SaveChangesAsync(ct);
        return id.Value;
    }
}
```

The `AnyAsync` check gives a clear 422 in the normal case. But it's check-then-insert, so two clerks adding
the same SKU at the same instant could both pass it. The unique index catches the second one, and
PostgreSQL reports error `23505`, "unique violation". Teach the exception handler to answer that with a 409
instead of a 500. In `GlobalExceptionHandler`'s status switch, after the concurrency line:

```csharp
// A unique index caught what a check-then-insert missed: two people creating the same thing at once.
DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: "23505" } }
                             => (StatusCodes.Status409Conflict, "That already exists. Reload and try again."),
```

The host already has Npgsql's exception type, through lesson 14's `Npgsql.OpenTelemetry` package.

```csharp
// src/Modules/Inventory/Erp.Modules.Inventory/Features/CreateItem/CreateItemEndpoint.cs
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Inventory.Features;


internal sealed record CreateItemRequest(string Sku, string Name, string Unit, bool AllowsFractions);

internal sealed class CreateItemRequestValidator : AbstractValidator<CreateItemRequest>
{
    public CreateItemRequestValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Unit).NotEmpty().MaximumLength(10);
    }
}

internal static class CreateItemEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/items", async Task<Created<CreatedId>> (CreateItemRequest body, CreateItemHandler handler, CancellationToken ct) =>
            {
                var id = await handler.HandleAsync(new CreateItemCommand(body.Sku, body.Name, body.Unit, body.AllowsFractions), ct);
                return TypedResults.Created($"/api/items/{id}", new CreatedId(id));
            })
            .RequireAuthorization(p => p.RequireRole(Roles.InventoryClerk, Roles.Admin));
}
```

The other three writes are short, and look like everything you've written since lesson 10. In
`Features/UpdateItem/`, an `UpdateItemCommand(long ItemId, string Name, string Unit, bool AllowsFractions)`
with the same validator rules, whose handler loads the item with `FindAsync`, calls `item.Describe(...)` and
saves; `PUT /items/{id:long}` takes an `UpdateItemRequest(string Name, string Unit, bool AllowsFractions)`.
In `Features/SetItemActive/`, a `SetItemActiveCommand(long ItemId, bool Active)` whose handler calls
`item.SetActive(cmd.Active)`, mapped as `POST /items/{id:long}/activate` and `/deactivate`. All for
`Roles.InventoryClerk, Roles.Admin`. And the adjustment:

```csharp
// src/Modules/Inventory/Erp.Modules.Inventory/Features/AdjustStock/AdjustStockHandler.cs
using Erp.Modules.Inventory.Domain;
using Erp.Modules.Inventory.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Inventory.Features;

internal sealed record AdjustStockCommand(long ItemId, decimal Quantity, string Reason);

internal sealed class AdjustStockHandler(InventoryDbContext db, ICurrentUser user, TimeProvider time)
{
    public async Task<bool> HandleAsync(AdjustStockCommand cmd, CancellationToken ct)
    {
        var item = await db.Items.FindAsync([new ItemId(cmd.ItemId)], ct);
        if (item is null) return false;
        db.StockMovements.Add(item.Adjust(cmd.Quantity, cmd.Reason, new UserId(user.UserId), time.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);   // xmin: a reservation that landed meanwhile makes this a 409
        return true;
    }
}
```

`POST /items/{id:long}/adjustments` takes an `AdjustStockRequest(decimal Quantity, string Reason)`. The
item and its movement are saved together, so there's never a count change without its audit row.

## 6. Reading the catalog

Two functions, the pattern from lesson 08:

```sql
-- inventory.search_items.v1.sql
-- Released scripts are never edited: change a function with a .v2 file and a new migration.
CREATE OR REPLACE FUNCTION inventory.search_items(p_search text, p_active_only boolean, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, sku text, name text, unit text, allows_fractions boolean, is_active boolean,
               on_hand numeric, reserved numeric)
LANGUAGE sql STABLE
AS $$
    SELECT i.id, i.sku::text, i.name::text, i.unit::text, i.allows_fractions, i.is_active, i.on_hand, i.reserved
    FROM inventory.item i
    WHERE (p_search IS NULL OR i.sku ILIKE '%' || p_search || '%' OR i.name ILIKE '%' || p_search || '%')
      AND (NOT p_active_only OR i.is_active)
    ORDER BY i.name, i.id
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;

CREATE OR REPLACE FUNCTION inventory.count_items(p_search text, p_active_only boolean)
RETURNS bigint
LANGUAGE sql STABLE
AS $$
    SELECT COUNT(*)
    FROM inventory.item i
    WHERE (p_search IS NULL OR i.sku ILIKE '%' || p_search || '%' OR i.name ILIKE '%' || p_search || '%')
      AND (NOT p_active_only OR i.is_active);
$$;
```

```sql
-- inventory.get_item_movements.v1.sql
-- Released scripts are never edited: change a function with a .v2 file and a new migration.
CREATE OR REPLACE FUNCTION inventory.get_item_movements(p_item_id bigint, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, quantity numeric, kind integer, reference text, reason text, user_id bigint, occurred_utc timestamptz)
LANGUAGE sql STABLE
AS $$
    SELECT m.id, m.quantity, m.kind, m.reference::text, m.reason::text, m.user_id, m.occurred_utc
    FROM inventory.stock_movement m
    WHERE m.item_id = p_item_id
    ORDER BY m.occurred_utc DESC, m.id DESC
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;

CREATE OR REPLACE FUNCTION inventory.count_item_movements(p_item_id bigint)
RETURNS bigint
LANGUAGE sql STABLE
AS $$
    SELECT COUNT(*) FROM inventory.stock_movement m WHERE m.item_id = p_item_id;
$$;
```

Apply both with an `AddReadFunctions` migration in Inventory, filled in by hand like Sales'.

```csharp
// src/Modules/Inventory/Erp.Modules.Inventory/Features/SearchItems/SearchItemsHandler.cs
using Dapper;
using Erp.Modules.Inventory.Domain;
using Erp.Shared.BuildingBlocks;
using FluentValidation;

namespace Erp.Modules.Inventory.Features;

internal sealed record SearchItemsQuery(string? Search = null, bool ActiveOnly = false, int Page = 1, int PageSize = 20);

internal sealed record ItemDto(
    long Id, string Sku, string Name, string Unit, bool AllowsFractions, bool IsActive,
    decimal OnHand, decimal Reserved, decimal Available);

internal sealed record ItemRow(long Id, string Sku, string Name, string Unit, bool AllowsFractions, bool IsActive, decimal OnHand, decimal Reserved);

internal sealed class SearchItemsValidator : AbstractValidator<SearchItemsQuery>
{
    public SearchItemsValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
    }
}

internal sealed class SearchItemsHandler(IDbConnectionFactory connections)
{
    public async Task<PagedResult<ItemDto>> HandleAsync(SearchItemsQuery q, CancellationToken ct)
    {
        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM inventory.search_items(@Search, @ActiveOnly, @Page, @PageSize);
            SELECT inventory.count_items(@Search, @ActiveOnly);
            """,
            new { Search = SearchText.ForLike(q.Search), q.ActiveOnly, q.Page, q.PageSize },
            cancellationToken: ct));

        var items = (await multi.ReadAsync<ItemRow>())
            .Select(r => new ItemDto(r.Id, r.Sku, r.Name, r.Unit, r.AllowsFractions, r.IsActive, r.OnHand, r.Reserved, r.OnHand - r.Reserved))
            .ToList();
        return new PagedResult<ItemDto>(items, q.Page, q.PageSize, (int)await multi.ReadSingleAsync<long>());
    }
}
```

`GET /items` (with `search`, `activeOnly`, `page`, `pageSize`) and `GET /items/{id:long}/movements` are open
to anyone signed in: a rep checks what's in stock, and a buyer will pick items for purchase orders. The
movements slice, `Features/GetItemMovements/`, maps each row's `kind` number back to its name with
`((MovementKind)r.Kind).ToString()`, the way lesson 08 did for order statuses.

## 7. Sales keeps its copy in step

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Infrastructure/EventHandlers/ItemCatalogChangedHandler.cs
using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Infrastructure;

// Keeps Sales' copy of the catalog in step with Inventory. It writes the full state each time, so a repeated or
// late delivery leaves the same result: idempotent.
internal sealed class ItemCatalogChangedHandler(SalesDbContext db) : IIntegrationEventHandler<ItemCatalogChanged>
{
    public async Task HandleAsync(ItemCatalogChanged e, CancellationToken ct)
    {
        var item = await db.SalesItems.FindAsync([new ItemId(e.ItemId)], ct);
        if (item is null)
            db.SalesItems.Add(SalesItem.FromCatalog(new ItemId(e.ItemId), e.Sku, e.Name, e.Unit, e.AllowsFractions, e.IsActive));
        else
            item.ApplyCatalog(e.Sku, e.Name, e.Unit, e.AllowsFractions, e.IsActive);
        await db.SaveChangesAsync(ct);
    }
}
```

This is an **upsert**: insert if missing, update if present. Deliver the same event twice and the second
time just writes the same values again. That's the "idempotent handler" lesson 13 asked for, achieved by
carrying the full state instead of a change.

A new item arrives unpriced and not for sale (`FromCatalog` from lesson 04), so it can't land on an order by
accident. It waits for the sales manager.

Now the trap that cost a real afternoon while building this course. Run the app, add an item, and wait.
Nothing reaches Sales. No error, no warning: the outbox marks the message processed, and no handler ran.

The cause is in `Program.cs`. The Mediator source generator only knows the messages in the assemblies you
list in `options.Assemblies`. `ItemCatalogChanged` lives in `Erp.Modules.Inventory.Contracts`, which wasn't
listed, so when the dispatcher published it, the generated mediator didn't know it had any handler at all.
The fix:

```csharp
// Every assembly that holds a message or a handler. Contracts hold the integration events: leave one out and
// publishing its events silently reaches nobody.
options.Assemblies =
[
    typeof(IdentityModule), typeof(SalesModule), typeof(InventoryModule),
    typeof(Erp.Modules.Inventory.Contracts.ItemCatalogChanged)
];
```

[Lesson 13](13-events-outbox.md)'s `Every_event_has_a_handler` test didn't catch this, because a handler *exists*. What catches it
is a test through the front door: create an item as the clerk, and wait for it to appear in the price list.
That's the integration test in step 10.

## 8. The price list and categories

Categories get an `Update`, the same refactor as `Customer` in lesson 15. Before, lesson 04's `Create`
checked the rules itself; after:

```csharp
public static ItemCategory Create(string name, decimal maxQuantityPerOrder, string? requiredRole)
{
    var category = new ItemCategory();
    category.Update(name, maxQuantityPerOrder, requiredRole);
    return category;
}

public void Update(string name, decimal maxQuantityPerOrder, string? requiredRole)
{
    if (string.IsNullOrWhiteSpace(name)) throw new DomainException(SalesErrors.CategoryNameRequired, "A category needs a name.");
    if (maxQuantityPerOrder <= 0) throw new DomainException(SalesErrors.CategoryMaxQuantityInvalid, "The maximum quantity per order must be greater than zero.");
    if (requiredRole is not null && !Roles.All.Contains(requiredRole))
        throw new DomainException(SalesErrors.UnknownRole, $"'{requiredRole}' is not a role.");
    Name = name.Trim();
    MaxQuantityPerOrder = maxQuantityPerOrder;
    RequiredRole = requiredRole;
}
```

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Features/SetItemPricing/SetItemPricingHandler.cs
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;

namespace Erp.Modules.Sales.Features;

internal sealed record SetItemPricingCommand(long ItemId, decimal UnitPrice, long CategoryId, bool IsForSale) : ICommand;

internal sealed class SetItemPricingValidator : AbstractValidator<SetItemPricingCommand>
{
    public SetItemPricingValidator()
    {
        RuleFor(x => x.ItemId).GreaterThan(0);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CategoryId).GreaterThan(0);
    }
}

internal sealed class SetItemPricingHandler(SalesDbContext db) : ICommandHandler<SetItemPricingCommand>
{
    public async ValueTask<Unit> Handle(SetItemPricingCommand cmd, CancellationToken ct)
    {
        var item = await db.SalesItems.FindAsync([new ItemId(cmd.ItemId)], ct)
            ?? throw new NotFoundException($"Item {cmd.ItemId} not found. New catalog items appear here within a few seconds.");
        var category = await db.ItemCategories.FindAsync([new CategoryId(cmd.CategoryId)], ct)
            ?? throw new NotFoundException($"Category {cmd.CategoryId} not found.");

        item.SetPricing(new Money(cmd.UnitPrice), category, cmd.IsForSale);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
```

Look at the not-found message. Eventual consistency has a visible edge: a manager who prices an item the
second the clerk saves it might beat the event. The message says exactly that, instead of a bare 404.

`PUT /price-list/{itemId:long}` takes a `SetItemPricingRequest(decimal UnitPrice, long CategoryId, bool
IsForSale)`, for `SalesRoles.Managers`. Changing a price never changes an existing order: lesson 04's lines
keep the price as ordered.

The categories slice puts list, create and edit in one feature folder, `Features/ItemCategories/`:

```csharp
// src/Modules/Sales/Erp.Modules.Sales/Features/ItemCategories/ItemCategoriesHandler.cs
using Dapper;
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Sales.Features;

internal sealed record ItemCategoryDto(long Id, string Name, decimal MaxQuantityPerOrder, string? RequiredRole);

internal sealed class ListItemCategoriesHandler(IDbConnectionFactory connections)
{
    public async Task<IReadOnlyList<ItemCategoryDto>> HandleAsync(CancellationToken ct)
    {
        await using var conn = connections.Create();
        return (await conn.QueryAsync<ItemCategoryDto>(new CommandDefinition(
            "SELECT * FROM sales.list_item_categories();", cancellationToken: ct))).ToList();
    }
}

internal sealed record SaveItemCategoryCommand(long? CategoryId, string Name, decimal MaxQuantityPerOrder, string? RequiredRole);

// One command for create (no id) and edit (id): a category has nothing else to its life.
internal sealed class SaveItemCategoryHandler(SalesDbContext db)
{
    // null: the category to update doesn't exist.
    public async Task<long?> HandleAsync(SaveItemCategoryCommand cmd, CancellationToken ct)
    {
        var role = string.IsNullOrWhiteSpace(cmd.RequiredRole) ? null : cmd.RequiredRole;
        var name = cmd.Name.Trim();
        if (await db.ItemCategories.AnyAsync(c => c.Name == name && c.Id != new CategoryId(cmd.CategoryId ?? 0), ct))
            throw new DomainException(SalesErrors.CategoryNameTaken, $"A category named '{name}' already exists.");

        ItemCategory category;
        if (cmd.CategoryId is { } id)
        {
            var existing = await db.ItemCategories.FindAsync([new CategoryId(id)], ct);
            if (existing is null) return null;
            category = existing;
            category.Update(name, cmd.MaxQuantityPerOrder, role);
        }
        else
        {
            category = ItemCategory.Create(name, cmd.MaxQuantityPerOrder, role);
            db.ItemCategories.Add(category);
        }

        await db.SaveChangesAsync(ct);
        return category.Id.Value;
    }
}
```

The `if (cmd.CategoryId is { } id)` chooses between *create* and *edit*; it isn't a business rule, so it's
allowed in a handler. The endpoints map `GET /item-categories` (Sales readers), `POST /item-categories` and
`PUT /item-categories/{id:long}` (managers), all taking an `ItemCategoryRequest(string Name, decimal
MaxQuantityPerOrder, string? RequiredRole)`. The list function is one more file, applied by a Sales migration
named `AddCategoryList`:

```sql
-- sales.list_item_categories.v1.sql
-- Released scripts are never edited: change a function with a .v2 file and a new migration.
CREATE OR REPLACE FUNCTION sales.list_item_categories()
RETURNS TABLE (id bigint, name text, max_quantity_per_order numeric, required_role text)
LANGUAGE sql STABLE
AS $$
    SELECT c.id, c.name::text, c.max_quantity_per_order, c.required_role::text
    FROM sales.item_category c
    ORDER BY c.name;
$$;
```

Raising a category's cap, or making it manager-only, takes effect on the next order. Existing drafts are
checked against the new rules the next time they're saved, because `SetLines` asks the category every time.

## 9. The screens

Three shapes join `Models.cs`:

```csharp
public sealed record ItemCategory(long Id, string Name, decimal MaxQuantityPerOrder, string? RequiredRole);

public sealed record StockItem(long Id, string Sku, string Name, string Unit, bool AllowsFractions, bool IsActive,
    decimal OnHand, decimal Reserved, decimal Available);

public sealed record StockMovement(long Id, decimal Quantity, string Kind, string? Reference, string? Reason, long UserId, DateTime OccurredUtc);
```

### Items and stock

```razor
@* src/Erp.Web/Pages/Inventory/Items.razor *@
@page "/items"
@inject ApiClient Api
@inject IDialogService Dialogs

<PageTitle>Items and stock · ERP</PageTitle>

<MudStack Row="true" AlignItems="AlignItems.Center" Class="mb-4">
    <MudText Typo="Typo.h4">Items and stock</MudText>
    <MudSpacer />
    <AuthorizeView Roles="@Keepers">
        <MudButton OnClick="() => Edit(null)" Variant="Variant.Filled" Color="Color.Primary" StartIcon="@Icons.Material.Filled.Add">New item</MudButton>
    </AuthorizeView>
</MudStack>

<MudStack Row="true" Spacing="4" Class="mb-4">
    <MudTextField T="string" Value="_search" ValueChanged="OnSearch" Label="SKU or name" DebounceInterval="400" Clearable="true"
                  Adornment="Adornment.Start" AdornmentIcon="@Icons.Material.Filled.Search" />
    <MudSwitch T="bool" Value="_activeOnly" ValueChanged="OnActive" Label="Active only" Color="Color.Primary" />
</MudStack>

<MudTable T="StockItem" @ref="_table" ServerData="Load" Hover="true" Dense="true">
    <HeaderContent>
        <MudTh>SKU</MudTh><MudTh>Name</MudTh><MudTh>Unit</MudTh>
        <MudTh Class="money">On hand</MudTh><MudTh Class="money">Reserved</MudTh><MudTh Class="money">Available</MudTh><MudTh></MudTh>
    </HeaderContent>
    <RowTemplate>
        <MudTd>@context.Sku</MudTd>
        <MudTd>@context.Name @if (!context.IsActive) { <MudChip T="string" Size="Size.Small">Inactive</MudChip> }</MudTd>
        <MudTd>@context.Unit</MudTd>
        <MudTd Class="money">@Format.Qty(context.OnHand)</MudTd>
        <MudTd Class="money">@Format.Qty(context.Reserved)</MudTd>
        <MudTd Class="money"><b>@Format.Qty(context.Available)</b></MudTd>
        <MudTd>
            <MudMenu Icon="@Icons.Material.Filled.MoreVert" Size="Size.Small" Dense="true" aria-label="Actions">
                <MudMenuItem OnClick="() => Movements(context)">Movement history</MudMenuItem>
                <AuthorizeView Roles="@Keepers" Context="auth">
                    <MudMenuItem OnClick="() => Adjust(context)">Adjust stock</MudMenuItem>
                    <MudMenuItem OnClick="() => Edit(context)">Edit</MudMenuItem>
                    <MudMenuItem OnClick="() => ToggleActive(context)">@(context.IsActive ? "Deactivate" : "Activate")</MudMenuItem>
                </AuthorizeView>
            </MudMenu>
        </MudTd>
    </RowTemplate>
    <NoRecordsContent>No items match.</NoRecordsContent>
    <PagerContent><MudTablePager /></PagerContent>
</MudTable>

@code {
    private static readonly string Keepers = $"{Roles.InventoryClerk},{Roles.Admin}";
    private MudTable<StockItem> _table = default!;
    private string? _search;
    private bool _activeOnly;

    private Task<TableData<StockItem>> Load(TableState state, CancellationToken ct) =>
        Api.Page<StockItem>(Url.With("api/items", ("search", _search), ("activeOnly", _activeOnly)), state);

    private Task OnSearch(string? text) { _search = text; return _table.ReloadServerData(); }
    private Task OnActive(bool value) { _activeOnly = value; return _table.ReloadServerData(); }

    private async Task Edit(StockItem? item)
    {
        var dialog = await Dialogs.ShowAsync<ItemDialog>(item is null ? "New item" : $"Edit {item.Sku}",
            new DialogParameters<ItemDialog> { { d => d.Item, item } });
        if ((await dialog.Result) is { Canceled: false }) await _table.ReloadServerData();
    }

    private async Task Adjust(StockItem item)
    {
        var dialog = await Dialogs.ShowAsync<AdjustStockDialog>($"Adjust {item.Sku}",
            new DialogParameters<AdjustStockDialog> { { d => d.Item, item } });
        if ((await dialog.Result) is { Canceled: false }) await _table.ReloadServerData();
    }

    private Task Movements(StockItem item) =>
        Dialogs.ShowAsync<MovementsDialog>($"{item.Sku} · {item.Name}",
            new DialogParameters<MovementsDialog> { { d => d.Item, item } },
            new DialogOptions { MaxWidth = MaxWidth.Large, FullWidth = true, CloseButton = true });

    private async Task ToggleActive(StockItem item)
    {
        if (await Api.Post($"api/items/{item.Id}/{(item.IsActive ? "deactivate" : "activate")}", success: "Saved."))
            await _table.ReloadServerData();
    }
}
```

The three dialogs are short. The item form edits SKU only when it's new (the SKU never changes):

```razor
@* src/Erp.Web/Pages/Inventory/ItemDialog.razor *@
@inject ApiClient Api

<MudDialog>
    <DialogContent>
        <MudStack Spacing="2" Style="min-width:380px">
            <MudTextField @bind-Value="_sku" Label="SKU" MaxLength="50" Disabled="Item is not null"
                          HelperText="@(Item is null ? "Can't be changed later" : null)" />
            <MudTextField @bind-Value="_name" Label="Name" MaxLength="200" />
            <MudTextField @bind-Value="_unit" Label="Unit" MaxLength="10" HelperText="pc, box, kg, m, L…" />
            <MudSwitch @bind-Value="_fractions" Label="Sold and counted in fractions (2.5 kg)" Color="Color.Primary" />
        </MudStack>
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="() => Dialog.Cancel()">Cancel</MudButton>
        <MudButton OnClick="Save" Variant="Variant.Filled" Color="Color.Primary"
                   Disabled="string.IsNullOrWhiteSpace(_sku) || string.IsNullOrWhiteSpace(_name) || string.IsNullOrWhiteSpace(_unit)">Save</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = default!;
    [Parameter] public StockItem? Item { get; set; }

    private string _sku = "", _name = "", _unit = "pc";
    private bool _fractions;

    protected override void OnInitialized()
    {
        if (Item is { } i) (_sku, _name, _unit, _fractions) = (i.Sku, i.Name, i.Unit, i.AllowsFractions);
    }

    private async Task Save()
    {
        var saved = Item is null
            ? await Api.Create("api/items", new { sku = _sku, name = _name, unit = _unit, allowsFractions = _fractions },
                "Item added. Set its price in the price list.") is not null
            : await Api.Put($"api/items/{Item.Id}", new { name = _name, unit = _unit, allowsFractions = _fractions }, "Item saved.");
        if (saved) Dialog.Close(DialogResult.Ok(true));
    }
}
```

The success message tells the clerk what happens next: Sales still has to price it.

```razor
@* src/Erp.Web/Pages/Inventory/AdjustStockDialog.razor *@
@inject ApiClient Api

@* A count correction: found, lost, damaged. Positive adds, negative removes. Every adjustment is kept with its reason. *@
<MudDialog>
    <DialogContent>
        <MudStack Spacing="2" Style="min-width:380px">
            <MudText>On hand now: <b>@Format.Qty(Item.OnHand) @Item.Unit</b> (reserved @Format.Qty(Item.Reserved))</MudText>
            <MudNumericField @bind-Value="_change" Label="@($"Change ({Item.Unit}): + found, − lost or damaged")" />
            <MudTextField @bind-Value="_reason" Label="Reason" MaxLength="500" Lines="2" />
            <MudText Typo="Typo.body2">After: @Format.Qty(Item.OnHand + _change) @Item.Unit</MudText>
        </MudStack>
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="() => Dialog.Cancel()">Cancel</MudButton>
        <MudButton OnClick="Save" Variant="Variant.Filled" Color="Color.Primary"
                   Disabled="_change == 0 || string.IsNullOrWhiteSpace(_reason)">Adjust</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = default!;
    [Parameter] public StockItem Item { get; set; } = default!;

    private decimal _change;
    private string _reason = "";

    private async Task Save()
    {
        if (await Api.Post($"api/items/{Item.Id}/adjustments", new { quantity = _change, reason = _reason }, "Stock adjusted."))
            Dialog.Close(DialogResult.Ok(true));
    }
}
```

```razor
@* src/Erp.Web/Pages/Inventory/MovementsDialog.razor *@
@inject ApiClient Api
@inject UserDirectory Users

<MudDialog>
    <DialogContent>
        <MudTable T="StockMovement" ServerData="Load" Dense="true" Hover="true" Elevation="0">
            <HeaderContent>
                <MudTh>When</MudTh><MudTh>What</MudTh><MudTh Class="money">Quantity</MudTh><MudTh>Reference / reason</MudTh><MudTh>By</MudTh>
            </HeaderContent>
            <RowTemplate>
                <MudTd>@Format.Date(context.OccurredUtc)</MudTd>
                <MudTd>@context.Kind</MudTd>
                <MudTd Class="money">@(context.Quantity > 0 ? "+" : "")@Format.Qty(context.Quantity) @Item.Unit</MudTd>
                <MudTd>@(context.Reference ?? context.Reason)</MudTd>
                <MudTd>@_names.GetValueOrDefault(context.UserId)</MudTd>
            </RowTemplate>
            <NoRecordsContent>No movements yet.</NoRecordsContent>
            <PagerContent><MudTablePager /></PagerContent>
        </MudTable>
    </DialogContent>
</MudDialog>

@code {
    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = default!;
    [Parameter] public StockItem Item { get; set; } = default!;
    private readonly Dictionary<long, string> _names = [];

    private async Task<TableData<StockMovement>> Load(TableState state, CancellationToken ct)
    {
        var page = await Api.Page<StockMovement>($"api/items/{Item.Id}/movements", state);
        foreach (var id in page.Items!.Select(m => m.UserId).Distinct()) _names[id] = await Users.NameOf(id);
        return page;
    }
}
```

The "By" column is lesson 16's user directory at work again: the movement stores an id, the screen shows
"Carlo Bautista".

### The price list

The price list page is the order list's pattern with a tab for categories: `src/Erp.Web/Pages/Sales/PriceList.razor`
at `/price-list`, a `MudTabs` with an **Items** tab (a server-paged `MudTable<PriceListItem>` over
`api/price-list` with a search box and a "For sale only" switch; each row shows SKU, name, unit, category,
price and "for sale", and a **Set price** button for managers) and a **Categories** tab holding a
`<Categories />` component. **Set price** opens a `PricingDialog` with a price field, a category
`MudSelect` filled from `api/item-categories`, and a "For sale" switch, which saves with
`Api.Put($"api/price-list/{Item.Id}", ...)`. The `Categories` component lists categories in a
`MudSimpleTable` and, for managers, shows a small form under it that creates or edits one. All three are
built from pieces you've used several times now; type them out, and compare with
[PriceList.razor](../course-validation/business/src/Erp.Web/Pages/Sales/PriceList.razor),
[PricingDialog.razor](../course-validation/business/src/Erp.Web/Pages/Sales/PricingDialog.razor) and
[Categories.razor](../course-validation/business/src/Erp.Web/Pages/Sales/Categories.razor) when you're done,
skipping their VAT and SC/PWD columns, the "VAT treatment" select, the special-discount switch and the
Special discounts tab: those arrive in lessons 20 and 21.

One line deserves a sentence. At the top of the price list:

```razor
<MudText Typo="Typo.body2" Class="mb-4">
    New items appear here a few seconds after they're added in Inventory, without a price. Give them a price and a
    category, and put them on sale.
</MudText>
```

Eventual consistency, explained to the person who'll see it. A screen should never leave a user guessing
why something isn't there yet.

### The menu

In `NavMenu.razor`, the Sales group gets **Price list**, and Inventory gets its own group:

```razor
<MudNavLink Href="price-list">Price list</MudNavLink>
```

```razor
<AuthorizeView Roles="@Stock">
    <MudNavGroup Title="Inventory" Icon="@Icons.Material.Filled.Inventory2" Expanded="true">
        <MudNavLink Href="items">Items and stock</MudNavLink>
    </MudNavGroup>
</AuthorizeView>
```

```csharp
private static readonly string Stock = Join(Roles.InventoryClerk, Roles.Admin, Roles.Purchaser, Roles.SalesManager, Roles.SalesRep);
```

## 10. Tests

A unit test project for Inventory, set up like lesson 16's for Identity, with four tests:

```csharp
// tests/Erp.Modules.Inventory.UnitTests/ItemTests.cs
using AwesomeAssertions;
using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Inventory.Domain;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Inventory.UnitTests;

public class ItemTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly UserId Clerk = new(4);

    private static Item Widget() => Item.Create(new ItemId(1), " gen-001 ", "Widget", "PC", allowsFractions: false);

    [Fact]
    public void A_new_item_is_tidied_and_announced()
    {
        var item = Widget();

        item.Sku.Should().Be("GEN-001");
        item.Unit.Should().Be("pc");
        item.IsActive.Should().BeTrue();
        item.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ItemCatalogChanged>()
            .Which.Name.Should().Be("Widget");
    }

    [Fact]
    public void An_adjustment_needs_a_reason_and_records_a_movement()
    {
        var item = Widget();

        item.Invoking(i => i.Adjust(10, " ", Clerk, Now)).Should().Throw<DomainException>();
        var movement = item.Adjust(10, "Opening count", Clerk, Now);

        item.OnHand.Should().Be(10);
        movement.Kind.Should().Be(MovementKind.Adjustment);
        movement.Quantity.Should().Be(10);
    }

    [Fact]
    public void Stock_cannot_be_adjusted_below_zero()
    {
        var item = Widget();
        item.Adjust(5, "Count", Clerk, Now);
        item.Invoking(i => i.Adjust(-6, "Damaged", Clerk, Now)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Whole_unit_items_refuse_fractional_adjustments()
    {
        Widget().Invoking(i => i.Adjust(1.5m, "Count", Clerk, Now)).Should().Throw<DomainException>().WithMessage("*whole*");
        Item.Create(new ItemId(5), "BLK-001", "Copper Wire", "kg", allowsFractions: true).Adjust(1.5m, "Count", Clerk, Now)
            .Quantity.Should().Be(1.5m);
    }
}
```

The second test is the one that caught `Adjust` changing the count before checking the reason: after the
refused call, `OnHand` was 10, then 20.

The architecture tests already list Inventory's Contracts assembly, so `Every_event_has_a_handler` now checks
`ItemCatalogChanged` too, and finds Sales' handler.

And the integration test that would have caught the Mediator trap, in `BusinessFlowTests.cs`:

```csharp
[Fact]
public async Task A_new_catalog_item_reaches_the_price_list_unpriced_and_can_be_priced()
{
    var clerk = await factory.ClientFor("clerk@erp.local");
    var manager = await factory.ClientFor("manager@erp.local");
    var sku = $"T-{Guid.NewGuid():N}"[..20];

    var itemId = await Create(clerk, "/api/items", new { sku, name = "Test Bracket", unit = "pc", allowsFractions = false });

    var listed = await Eventually(
        () => manager.GetFromJsonAsync<Page<PriceRow>>($"/api/price-list?search={sku}")!,
        p => p!.TotalCount == 1);
    listed!.Items.Single().IsForSale.Should().BeFalse("a new item waits for a price");

    (await manager.PutAsJsonAsync($"/api/price-list/{itemId}", new { unitPrice = 12.5m, categoryId = 1, isForSale = true }))
        .StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await clerk.PostAsJsonAsync("/api/items", new { sku, name = "Duplicate", unit = "pc", allowsFractions = false }))
        .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
}
```

`Eventually` asks again every few hundred milliseconds until the condition holds, giving up after ten
seconds: the right way to test something that happens "a moment later". `Create`, `Page`, `PriceRow` and
`Eventually` are small helpers at the top of the test class; copy them from
[BusinessFlowTests.cs](../course-validation/business/tests/Erp.Integration.Tests/BusinessFlowTests.cs).

In the UI tests, add `"/price-list"` and `"/items"` to `pages`.

## 11. Prove it: two people, one SKU

The unique index on `sku` is what really stops duplicates; the handler's "does it exist?" check is only for
a friendly message. A check followed by an insert has a gap, and two requests at once can both pass the
check. So test the gap, in `LaterModuleEdgeCaseTests`:

```csharp
// Lesson 17: two people adding the same SKU at once; the unique index catches what the check missed.
[Fact]
public async Task The_same_sku_twice_is_409()
{
    var admin = await factory.ClientFor("admin@erp.local");
    var item = new { sku = $"DUP-{Guid.NewGuid():N}"[..20], name = "Duplicate", unit = "pc", allowsFractions = false };

    var responses = await Task.WhenAll(admin.PostAsJsonAsync("/api/items", item), admin.PostAsJsonAsync("/api/items", item));

    responses.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.Created, HttpStatusCode.Conflict]);
}
```

One 201 and one 409, in either order: that's `23505 → 409` in `GlobalExceptionHandler`, doing its job.
Without that line, the loser would be a 500.

`ItemCatalogChanged` is the first event whose handler *writes* (Sales' copy of the item). In
`Delivering_every_event_a_second_time_changes_nothing`, add the tables it touches to the snapshot:

```csharp
|| '|' || (SELECT COUNT(*) FROM sales.item)::text
|| '|' || (SELECT COALESCE(SUM(on_hand), 0) FROM inventory.item)::text
```

Redelivering `ItemCatalogChanged` must update the same copy, never add a second.

| Test | The edge | Why it matters |
|---|---|---|
| `The_same_sku_twice_is_409` | At the same instant | Two items with one SKU break every lookup by SKU |
| `Stock_cannot_be_adjusted_below_zero` | Below zero | Negative stock means the books are wrong |
| `Whole_unit_items_refuse_fractional_adjustments` | Too precise | Half a server rack |
| `An_adjustment_needs_a_reason_and_records_a_movement` | Nothing | An unexplained change to stock is what auditors look for |
| `A_new_catalog_item_reaches_the_price_list_unpriced_and_can_be_priced` | Across modules, through an event | The copy arrives, and can't be sold until priced |
| The snapshot lines in the redelivery test | Twice | The upsert handler is idempotent |

## How a request flows

```mermaid
sequenceDiagram
  participant C as Clerk
  participant I as Inventory (CreateItemHandler)
  participant IO as inventory.outbox_message
  participant D as Outbox dispatcher
  participant S as Sales (ItemCatalogChangedHandler)
  C->>I: POST /api/items { sku: "GEN-004", ... }
  I->>I: nextval(item_id_seq) → 6; Item.Create raises ItemCatalogChanged(6, ...)
  I->>IO: INSERT item + INSERT outbox row (one transaction)
  I-->>C: 201 { id: 6 }
  D->>IO: take the message (a moment later)
  D->>S: ItemCatalogChanged(6, "GEN-004", ...)
  S->>S: no item 6 yet → SalesItem.FromCatalog (unpriced, not for sale)
```

## Mistakes to avoid

- Letting two modules change the same fact. One owner per fact; everyone else keeps a copy.
- A `JOIN` into another module's schema to show a name. Keep a copy, kept in step by an event.
- Events that carry only what changed. Carry the full state, so the handler can apply it twice safely.
- Forgetting the Contracts assembly in `options.Assemblies`. The events are published to nobody, silently.
- Raising an event with an id the database hasn't assigned yet. Take it from a sequence first.
- Seeding explicit ids without `setval`. The first real insert collides.
- Changing a count before checking the reason. Check everything, then change.

## Let's run it!

Drop the `erp` database (step 3 explains why), then start the app. Sign in as `clerk@erp.local`:

1. **Inventory → Items and stock**. The five seeded items, with their stock. Copper Wire shows 250 kg.
2. **New item**: SKU `gen-004`, name "Bracket", unit `PC`. It's saved as `GEN-004`, unit `pc`.
3. **Adjust stock** on the Bracket: +40, reason "Opening count". Then **Movement history**: one adjustment,
   +40 pc, "Opening count", by Carlo Bautista.
4. Try adjusting the Restricted Kit by −20: "would leave −12 pc on hand, less than the 0 reserved."

Now sign in as `manager@erp.local` and open **Sales → Price list**. The Bracket is there, priced ₱0.00, not
for sale. Click **Set price**: ₱35, category General, for sale. As `rep@erp.local`, create an order: the
item search finds "GEN-004 · Bracket".

Back as the clerk, rename the Bracket to "Wall Bracket". A moment later, the price list says "Wall
Bracket", with the price the manager set still there. The two modules own different facts about one item,
and neither overwrote the other's.

Last, as the rep, submit an order with 10 Brackets, then try to deactivate the Bracket as the clerk: "has 10
pc reserved for orders and can't be deactivated."

```bash
dotnet test
```

The four Inventory unit tests pass, and so does the catalog-to-price-list integration test. Remove the
Contracts assembly from `options.Assemblies` once, run that test, and watch it time out. Then put it back.

## Let's check!

1. Who owns an item's name, and who owns its price? Why not one module for both?
2. Why does Sales keep a copy of the catalog instead of asking Inventory each time?
3. What's the difference between a domain event and an integration event, and where does each live?
4. Why does `ItemCatalogChanged` carry the full item, and not just what changed?
5. Why does Inventory take an item's id from a sequence before creating it?
6. An item is added, but Sales never hears about it, and nothing reports an error. What's the first thing you
   check?
7. Why does the dev seed end with `setval`?

And that's where this lesson ends. The warehouse owns its catalog, every stock change has an audit trail,
and Sales' copy keeps itself in step. Next time, the clerk ships approved orders, and stock finally leaves
the building. Good luck, developer!

---

← [Lesson 16 — Users and email](16-users-email.md) · Next: [Lesson 18 — Shipping](18-shipping.md) →
