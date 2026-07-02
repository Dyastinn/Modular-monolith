# Phase 6 — The Read Side: Queries (CQRS-lite)

**Goal:** understand why queries are shaped differently from commands — they bypass the domain and
project straight into DTOs — and build `GetOrder` and `SearchOrders`.
**Produces:** `GetOrder` and `SearchOrders` query slices, read DTOs, read stored procedures, and the
query handlers that call Dapper directly.
**Prereq:** Phases 4–5 (you have orders to read).

> This phase is where "CQRS" becomes concrete — and where it's easy to over-reach. Re-read Tension D
> in the overview: we build **CQRS-lite** (separate handlers + read models, one database), not full
> CQRS (separate stores, event sourcing).

---

## Step 1 — The problem: forcing reads through the domain

If a query loads the full `Order` aggregate (rehydrate lines, value objects, invariants) just to show
a list, you pay for machinery you don't use. Worse, the aggregate's shape rarely matches what a screen
needs — a search grid wants `OrderId, CustomerName, Total, Status, CreatedDate`, flattened, maybe
joined across tables. Rebuilding aggregates to then flatten them is wasted work, and it couples your
read screens to your write model's shape.

## Step 2 — The insight: reads and writes have different needs

- **Writes** must protect invariants → go through the rich domain (Phases 3–5).
- **Reads** must be shaped for display and fast → skip the domain, query the exact columns into a DTO.

That asymmetry *is* CQRS-lite. The command side and query side share a database but not a model:

```
Command side:  Command -> Handler -> Domain aggregate -> Repository -> proc (write)
Query side:    Query   -> Handler -> Dapper -> proc (read) -> DTO      (no domain at all)
```

There is **no `Order` aggregate on the query path.** A query handler is allowed to hit Dapper directly,
because there are no invariants to protect when reading.

## Step 3 — Query slices

```
Application/Orders/GetOrder/
    GetOrderQuery.cs        <- record(long OrderId) : IQuery<OrderDetailsDto?>
    GetOrderHandler.cs      <- Dapper: usp_OrderDetails_Get -> OrderDetailsDto
    OrderDetailsDto.cs      <- flat read shape (header + line rows)
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
        return OrderDetailsDto.From(header, lines);   // plain mapping, no domain
    }
}
```

Notice the query handler depends on `IDbConnectionFactory`, not `IOrderRepository`. The repository is a
*write-side* concept (it deals in aggregates). Reads don't need it. Keeping them separate is what stops
your repository from growing thirty read methods with slightly different shapes — the layered
"repository does everything" bloat.

## Step 4 — Read stored procedures

Reads are where stored procs and set-based SQL shine — joins, paging, filtering:

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

This is a legitimate use of "keep in SQL what belongs in SQL": the `Total` here is a **read-time
projection for display**, computed by the DB set-based. That's not the split-brain problem — the
*authoritative* total for booking/approval decisions is still the domain's computed `Total`. The query
total is a denormalized convenience for a grid. If that distinction feels subtle, it is — write it down:
*the domain owns the total that drives decisions; SQL may re-project a total for display.*

> **Watch for drift here.** If the display total and the domain total can disagree in a way users
> notice, you have a consistency problem. For this project they compute the same thing the same way, so
> it's fine. In a real system you'd either always show the persisted total or reconcile deliberately.

## Step 5 — What full CQRS would add (and why we don't)

To be explicit about what CQRS-lite is *not*, so you don't conflate them:

| Full CQRS adds | Cost | When it's worth it |
|---|---|---|
| Separate read store (denormalized) | A projection pipeline to keep it in sync | Read/write loads diverge hugely; reads need a different shape/scale |
| Event sourcing | Rebuild-from-events complexity, versioning | You need full audit/history or temporal queries |
| Eventual consistency | Reads can lag writes; UX must tolerate it | Scale demands it |

None of these are justified by this project's requirements. Adding them now would be architecture
astronautics — exactly the over-engineering your preferences warn against. CQRS-lite gives the
"features easy to locate, reads shaped for display" benefit you asked for without the operational tax.

## What Sonnet should produce

- `GetOrder` and `SearchOrders` slices with DTOs, handlers (Dapper direct), and read procs
  (`usp_OrderDetails_Get`, `usp_Orders_Search`).
- `PagedResult<T>` helper.
- `GET /orders/{id}` and `GET /orders?...` endpoints.
- A short comment/README note stating this is CQRS-lite and pointing at §5 for what full CQRS would add.

**NOT in this phase:** any domain aggregate on the read path; any write logic; caching (out of scope).

## Pitfalls

- **Routing queries through `IOrderRepository`.** Reads don't need aggregates; go straight to Dapper.
- **Reusing the write DTOs/records for reads.** Read models are shaped for screens and evolve
  independently; coupling them re-introduces the "change the write model, break every screen" problem.
- **Over-reaching into full CQRS** because it sounds more "correct." It isn't more correct here; it's
  more expensive.
- **Business logic in read procs.** Projection/aggregation for display is fine; decisions are not — a
  read query must never change state or enforce policy.

---

## Checkpoint — you understand this phase when you can answer:

1. Why does a query handler skip the domain and the repository?
2. What's the difference between the domain's `Total` and the search proc's `Total`, and why isn't that
   the split-brain problem?
3. What exactly does CQRS-lite give you here, and name two things full CQRS would add that you don't need.
4. Why not reuse the write-side model for reads?
