# Phase 6 — Reading Data: Why Queries Are Different (CQRS-lite)

> 📄 Expert version: [`../plan-Opus/07-phase-6-queries.md`](../plan-Opus/07-phase-6-queries.md)

**In one sentence:** Reading data doesn't need all the careful rule-protecting machinery that *changing*
data needs — so queries take a simpler, faster path that skips the domain entirely.

**What you'll build:** The `GetSalesOrder` and `SearchSalesOrders` features (two files each), read-only
DTOs, read stored procedures in the `sales` schema, and query handlers that call Dapper directly.

**Before you start:** Phases 4–5 (you have orders to read).

> This is where "CQRS" becomes concrete — and where it's easy to overdo it. We build **CQRS-lite**
> (separate read/write *code*, one shared database), **not** full CQRS (separate databases, event
> sourcing).

---

## Step 1 — The problem: forcing reads through the domain

Suppose you want a search grid of orders: `SalesOrderId, CustomerName, Total, Status, CreatedDate`.

If you load the *full* `SalesOrder` aggregate (rebuild every line, every value object, run every invariant)
just to display a row, you're paying for machinery you don't use. Worse, the aggregate's shape rarely
matches what the *screen* wants — the grid wants a flat row, maybe joined across tables. Rebuilding rich
aggregates only to flatten them is wasted work, and it chains your read screens to your write model's shape.

> 🧭 **WebForms echo:** this is the moment your instinct says "just `SELECT` the columns the grid needs and
> bind it" — and for *reads*, that instinct is exactly right. The discipline we added in Phases 3–5 was for
> the *write* side. Reads get to be simple again.

## Step 2 — The insight: reading and writing have different needs

- **Writing** must protect the rules → it goes through the rich domain (Phases 3–5).
- **Reading** must be shaped for the screen and fast → it skips the domain and grabs exactly the columns it
  needs into a plain DTO.

That asymmetry *is* CQRS-lite. The two sides share one database but not one model:

```
Writing:  Command → Handler → Domain aggregate → Repository → proc (write)
Reading:  Query   → Handler → Dapper → proc (read) → DTO      (no domain at all!)
```

> 🔑 There is **no `SalesOrder` aggregate on the read path.** A query handler is *allowed* to hit Dapper
> directly, because when you're only reading, there are no rules to protect. *(Bank teller vs. ATM screen:
> the teller carefully verifies everything to move money; the ATM just prints your balance.)*

## Step 3 — What a query feature looks like (still two files)

```
Modules/Sales/Features/GetSalesOrder/
    GetSalesOrderHandler.cs   <- Query + the read DTOs (Result) + Handler (Dapper direct)
    GetSalesOrderEndpoint.cs  <- the Carter endpoint
Modules/Sales/Features/SearchSalesOrders/
    SearchSalesOrdersHandler.cs
    SearchSalesOrdersEndpoint.cs
```

The read DTOs are the query's *result*, so they live in the handler file — same convention as commands, just
with a DTO instead of a domain result:

```csharp
// GetSalesOrderHandler.cs
namespace Erp.Modules.Sales.Features.GetSalesOrder;

public sealed record GetSalesOrderQuery(long SalesOrderId) : IQuery<SalesOrderDetailsDto?>;

public sealed record SalesOrderDetailsDto(long SalesOrderId, string CustomerName,
    string Status, decimal Total, IReadOnlyList<SalesOrderLineDto> Lines);
public sealed record SalesOrderLineDto(long ProductId, string ProductName, int Quantity, decimal UnitPrice);

public sealed class GetSalesOrderHandler(IDbConnectionFactory factory)
    : IQueryHandler<GetSalesOrderQuery, SalesOrderDetailsDto?>
{
    public async Task<SalesOrderDetailsDto?> Handle(GetSalesOrderQuery q, CancellationToken ct)
    {
        using var conn = factory.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            "sales.usp_SalesOrderDetails_Get", new { q.SalesOrderId },
            commandType: CommandType.StoredProcedure, cancellationToken: ct));

        var header = await multi.ReadSingleOrDefaultAsync<SalesOrderHeaderRow>();
        if (header is null) return null;
        var lines = (await multi.ReadAsync<SalesOrderLineRow>()).ToList();
        return SalesOrderDetailsDto.From(header, lines);   // plain mapping, zero domain
    }
}
```

### Reading the query handler line by line

```csharp
using var conn = factory.Create();
```
Get a raw database connection (`IDbConnectionFactory` from `Shared.Infrastructure`). No repository, no
`SalesOrder` object — just a connection.

```csharp
using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
    "sales.usp_SalesOrderDetails_Get", new { q.SalesOrderId }, ...));
```
Run the read proc, passing `@SalesOrderId`. `QueryMultipleAsync` because this proc returns *two* result
sets: the header and its lines.

```csharp
var header = await multi.ReadSingleOrDefaultAsync<SalesOrderHeaderRow>();
if (header is null) return null;
var lines = (await multi.ReadAsync<SalesOrderLineRow>()).ToList();
return SalesOrderDetailsDto.From(header, lines);
```
Read the header (or `null` = not found), then the lines, then mash them into one flat DTO shaped for the
screen. Dapper matches columns to fields by name.

**Database → DTO, no domain in sight.** Compare to Phase 4's write handler (`SalesOrder.Create`, `AddLine`,
transactions, repository...). The read side is *deliberately* simpler, because reading has nothing to
protect.

> 👀 The query handler depends on `IDbConnectionFactory`, **not** `ISalesOrderRepository`. The repository is
> a *write-side* tool (whole aggregates). Reads don't need it. Keeping them separate stops your repository
> from bloating into thirty slightly-different read methods.

> ❓ **"It feels wrong to skip the careful domain stuff. Isn't that cutting corners?"** It would be, on the
> *write* side. But a read *can't break any rules* — it changes nothing. Forcing a read through the rich
> domain wouldn't make it "safer," just slower and more coupled. Matching the simple tool to the read job
> isn't laziness.

> 🧱 **Reads respect the walls too.** A Sales query reads from the `sales` schema. If a screen needed
> *Inventory* data, the Sales query would not join across into `inventory.*` — it would ask Inventory
> (through a read contract, or compose two calls at the endpoint). Cross-schema joins quietly re-couple the
> modules; don't reach across the wall even for reads.

## Step 4 — Read stored procedures (where SQL shines)

Reading is exactly where set-based SQL is best — joins, paging, filtering, all in one query:

```sql
CREATE PROCEDURE sales.usp_SalesOrders_Search
    @CustomerId BIGINT = NULL,
    @Status     INT    = NULL,
    @Page       INT,
    @PageSize   INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT o.SalesOrderId, c.Name AS CustomerName, o.Status, o.CreatedUtc,
           SUM(l.Quantity * l.UnitPrice) AS Total
    FROM sales.SalesOrder o
    JOIN sales.Customer c ON c.CustomerId = o.CustomerId
    LEFT JOIN sales.SalesOrderLine l ON l.SalesOrderId = o.SalesOrderId
    WHERE (@CustomerId IS NULL OR o.CustomerId = @CustomerId)
      AND (@Status IS NULL OR o.Status = @Status)
    GROUP BY o.SalesOrderId, c.Name, o.Status, o.CreatedUtc
    ORDER BY o.CreatedUtc DESC
    OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
END
```

> ⚠️ **"Isn't computing `Total` here the split-brain problem from Phase 4?"** Good catch, and no. This
> `Total` is a **read-time projection for display**. The *authoritative* total that drives submit/approval
> decisions is still the domain's computed `Total`. One is for showing; the other for deciding. Write that
> down: *the domain owns the total that drives decisions; SQL may re-project a total just for display.*

## Step 5 — What *full* CQRS would add (and why we don't)

| Full CQRS adds | What it costs | When it's worth it |
|---|---|---|
| A separate read database (denormalized) | A pipeline to keep it in sync | Read and write loads diverge hugely |
| Event sourcing | Rebuild-from-events complexity, versioning | You need full audit/history or time-travel |
| Eventual consistency | Reads can lag writes; UX must tolerate it | Scale demands it |

**None** are justified here. Adding them now would be "architecture astronautics." CQRS-lite gives the real
benefit (features easy to find, reads shaped for the screen) without the operational tax.

## What to produce in this phase

- `GetSalesOrder` and `SearchSalesOrders` features (two files each) with DTOs, Dapper-direct handlers, and
  read procs (`sales.usp_SalesOrderDetails_Get`, `sales.usp_SalesOrders_Search`).
- A `PagedResult<T>` helper in `Shared.Common`.
- `GET /sales-orders/{id}` and `GET /sales-orders?...` Carter endpoints.
- A short note stating this is CQRS-lite and pointing at Step 5.

**NOT now:** any domain aggregate on the read path; any write logic; cross-schema joins into other modules;
caching.

## Common mistakes to avoid

- **Routing queries through `ISalesOrderRepository`.** Reads don't need aggregates — go straight to Dapper.
- **Reusing your write-side records for reads.** Read models are shaped for screens and change
  independently.
- **Joining across into another module's schema.** Re-couples modules; ask the owning module instead.
- **Reaching for full CQRS because it sounds more "correct."** It's just more expensive here.
- **Business logic in read procs.** Projecting/aggregating for display is fine; *deciding* is not.

---

## ✅ Check Yourself

1. **Why does a query handler skip both the domain and the repository?**
2. **What's the difference between the domain's `Total` and the search proc's `Total`, and why isn't that
   split-brain?**
3. **What exactly does CQRS-lite give you here, and name two things full CQRS would add that you don't
   need.**
4. **A Sales grid needs to show live stock levels (owned by Inventory). Why not just join into
   `inventory.Stock`, and what do you do instead?**

---

## 👨‍🏫 Answer Key

**1. Why does a query handler skip the domain and the repository?**
Because when only **reading**, there are no rules to protect — the rich-domain machinery is wasted work. The
domain guards *changes*; a read changes nothing. The repository deals in whole aggregates (write-side); a
screen wants a flat row. So the query handler goes **straight to Dapper**, runs a read proc, and maps rows to
a DTO. *(Teller vs. ATM.)*

**2. Domain `Total` vs. search-proc `Total` — why not split-brain?**
The **domain's `Total`** is *authoritative* and drives decisions (submit, approval). The **proc's `Total`**
is a *read-time display projection*. Not split-brain because they play different roles — one *decides*, one
*shows*. Split-brain is the same *decision* rule in two places that drift; here the decision rule lives in
exactly one place (the domain).

**3. What CQRS-lite gives you, and two things full CQRS adds.**
CQRS-lite: **separate simple read code** (fast, screen-shaped Dapper queries) + **separate careful write
code** (rich domain), sharing **one database**. Full CQRS adds (any two): a **separate read database** kept
in sync by a projection pipeline; **event sourcing**; **eventual consistency**. None justified here.

**4. Why not join into `inventory.Stock`, and what instead?**
Because a Sales query joining into Inventory's schema re-couples the two modules at the database level —
exactly the wall we built in Phase 5, quietly broken. Instead, ask Inventory: expose a small read contract
(or query endpoint) that returns stock for a set of product ids, and compose the two results (in the handler
or at the endpoint). The wall holds for reads as well as writes.

> ✅ **You're ready for Phase 7 if:** you can explain "reads skip the domain because there are no rules to
> protect," and you're comfortable that a display total and a decision total can coexist without being
> "split brain."
