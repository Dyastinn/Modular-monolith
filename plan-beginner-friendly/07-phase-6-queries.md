# Phase 6 — Reading Data: Why Queries Are Different (CQRS-lite)

> 📄 Expert version: [`../plan-Opus/07-phase-6-queries.md`](../plan-Opus/07-phase-6-queries.md)

**In one sentence:** Reading data doesn't need all the careful rule-protecting machinery that
*changing* data needs — so queries take a simpler, faster path that skips the domain entirely.

**What you'll build:** The `GetOrder` and `SearchOrders` features, read-only DTOs, read stored
procedures, and query handlers that call Dapper directly.

**Before you start:** Phases 4–5 (you have orders to read).

> This is where "CQRS" becomes concrete — and where it's easy to overdo it. We build **CQRS-lite**
> (separate read/write *code*, one shared database), **not** full CQRS (separate databases, event
> sourcing).

---

## Step 1 — The problem: forcing reads through the domain

Suppose you want to show a search grid of orders: `OrderId, CustomerName, Total, Status, CreatedDate`.

If you load the *full* `Order` aggregate (rebuild every line, every value object, run every invariant)
just to display a row, you're paying for a bunch of machinery you don't use. Worse, the aggregate's
shape rarely matches what the *screen* wants — the grid wants a flat row, maybe joined across several
tables. Rebuilding rich aggregates only to flatten them back down is wasted work, and it chains your
read screens to your write model's shape.

## Step 2 — The insight: reading and writing have different needs

- **Writing** must protect the rules → it goes through the rich domain (Phases 3–5).
- **Reading** must be shaped for the screen and fast → it skips the domain and grabs exactly the
  columns it needs into a plain DTO.

That asymmetry *is* CQRS-lite. The two sides share one database but not one model:

```
Writing:  Command → Handler → Domain aggregate → Repository → proc (write)
Reading:  Query   → Handler → Dapper → proc (read) → DTO      (no domain at all!)
```

> 🔑 There is **no `Order` aggregate on the read path.** A query handler is *allowed* to hit Dapper
> directly, because when you're only reading, there are no rules to protect. *(Bank teller vs. ATM
> screen: the teller carefully verifies everything to move money; the ATM just prints your balance.)*

## Step 3 — What a query feature looks like

```
Application/Orders/GetOrder/
    GetOrderQuery.cs        <- record(long OrderId) : IQuery<OrderDetailsDto?>
    GetOrderHandler.cs      <- Dapper: run usp_OrderDetails_Get → OrderDetailsDto
    OrderDetailsDto.cs      <- a flat read shape (header + line rows)
Application/Orders/SearchOrders/
    SearchOrdersQuery.cs    <- filters + paging : IQuery<PagedResult<OrderSummaryDto>>
    SearchOrdersHandler.cs
    OrderSummaryDto.cs
```

```csharp
public sealed record GetOrderQuery(long OrderId) : IQuery<OrderDetailsDto?>;

public sealed class GetOrderHandler(IDbConnectionFactory factory)
    : IRequestHandler<GetOrderQuery, OrderDetailsDto?>
{
    public async Task<OrderDetailsDto?> Handle(GetOrderQuery q, CancellationToken ct)
    {
        using var conn = factory.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            "usp_OrderDetails_Get", new { q.OrderId },
            commandType: CommandType.StoredProcedure, cancellationToken: ct));

        var header = await multi.ReadSingleOrDefaultAsync<OrderHeaderRow>();
        if (header is null) return null;
        var lines = (await multi.ReadAsync<OrderLineRow>()).ToList();
        return OrderDetailsDto.From(header, lines);   // plain mapping, zero domain
    }
}
```

> 👀 Notice the query handler depends on `IDbConnectionFactory`, **not** `IOrderRepository`. The
> repository is a *write-side* tool (it deals in whole aggregates). Reads don't need it. Keeping them
> separate is exactly what stops your repository from bloating into thirty slightly-different read
> methods.

## Step 4 — Read stored procedures (where SQL shines)

Reading is exactly where set-based SQL is best — joins, paging, filtering, all in one query:

```sql
CREATE PROCEDURE usp_Orders_Search
    @CustomerId BIGINT = NULL,
    @Status     INT    = NULL,
    @Page       INT,
    @PageSize   INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT o.OrderId, c.Name AS CustomerName, o.Status, o.CreatedUtc,
           SUM(l.Quantity * l.UnitPrice) AS Total
    FROM dbo.[Order] o
    JOIN dbo.Customer c ON c.CustomerId = o.CustomerId
    LEFT JOIN dbo.OrderLine l ON l.OrderId = o.OrderId
    WHERE (@CustomerId IS NULL OR o.CustomerId = @CustomerId)
      AND (@Status IS NULL OR o.Status = @Status)
    GROUP BY o.OrderId, c.Name, o.Status, o.CreatedUtc
    ORDER BY o.CreatedUtc DESC
    OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
END
```

> ⚠️ **"Wait — isn't computing `Total` here the split-brain problem from Phase 4?"** Good catch, and
> no. This `Total` is a **read-time projection for display** — a convenience number for a grid. The
> *authoritative* total that drives booking/approval decisions is still the domain's computed `Total`.
> One is for showing; the other is for deciding. Write that distinction down: *the domain owns the
> total that drives decisions; SQL may re-project a total just for display.*

> If the display total and the domain total could ever disagree in a way users notice, you'd have a
> real consistency problem. For this project they compute the same thing the same way, so it's fine.

## Step 5 — What *full* CQRS would add (and why we don't)

So you never confuse CQRS-lite with the big version, here's what the full thing adds:

| Full CQRS adds | What it costs | When it's worth it |
|---|---|---|
| A separate read database (denormalized) | A pipeline to keep it in sync | Read and write loads diverge hugely |
| Event sourcing | Rebuild-from-events complexity, versioning | You need full audit/history or time-travel queries |
| Eventual consistency | Reads can lag behind writes; the UX must tolerate it | Scale demands it |

**None** of these are justified by this project. Adding them now would be "architecture astronautics" —
over-engineering for problems you don't have. CQRS-lite gives you the real benefit you wanted (features
easy to find, reads shaped for the screen) without the operational tax.

## What to produce in this phase

- `GetOrder` and `SearchOrders` features with DTOs, handlers (Dapper direct), and read procs
  (`usp_OrderDetails_Get`, `usp_Orders_Search`).
- A `PagedResult<T>` helper.
- `GET /orders/{id}` and `GET /orders?...` endpoints.
- A short note (comment or README) stating this is CQRS-lite and pointing at Step 5.

**NOT now:** any domain aggregate on the read path; any write logic; caching (out of scope).

## Common mistakes to avoid

- **Routing queries through `IOrderRepository`.** Reads don't need aggregates — go straight to Dapper.
- **Reusing your write-side records for reads.** Read models are shaped for screens and change
  independently; coupling them re-creates "change the write model, break every screen."
- **Reaching for full CQRS because it sounds more "correct."** It isn't more correct here — just more
  expensive.
- **Business logic in read procs.** Projecting/aggregating for display is fine; *deciding* is not. A
  read query must never change state or enforce policy.

---

## ✅ Check Yourself

1. **Why does a query handler skip both the domain and the repository?**
2. **What's the difference between the domain's `Total` and the search proc's `Total`, and why isn't
   that the split-brain problem?**
3. **What exactly does CQRS-lite give you here, and name two things full CQRS would add that you don't
   need.**
4. **Why not reuse the write-side model for reads?**
