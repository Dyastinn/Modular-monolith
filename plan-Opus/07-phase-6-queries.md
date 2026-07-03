# Phase 6 — The Read Side: Queries (CQRS-lite)

**Goal:** understand why queries are shaped differently from commands — they bypass the domain and project
straight into DTOs — and build `GetSalesOrder` and `SearchSalesOrders` (two files each).
**Produces:** the two query slices, read DTOs, read stored procs (`sales` schema), Dapper-direct handlers.
**Prereq:** Phases 4–5.

> CQRS-lite (separate handlers + read models, one database), not full CQRS (Tension D).

---

## Step 1 — The problem: forcing reads through the domain

Loading the full `SalesOrder` aggregate to show a grid pays for machinery you don't use, and the aggregate
shape rarely matches the screen (`SalesOrderId, CustomerName, Total, Status, CreatedDate`, flattened,
joined). Rebuilding aggregates to flatten them is wasted work and couples read screens to the write model.

## Step 2 — The insight

- **Writes** protect invariants → rich domain.
- **Reads** are shaped for display and fast → skip the domain, query columns into a DTO.

```
Command side:  Command -> Handler -> Domain -> Repository -> proc (write)
Query side:    Query   -> Handler -> Dapper -> proc (read) -> DTO      (no domain)
```

No `SalesOrder` aggregate on the query path — no invariants to protect when reading.

## Step 3 — Query slices (two files; DTO is the result, in the handler file)

```csharp
// GetSalesOrderHandler.cs
public sealed record GetSalesOrderQuery(long SalesOrderId) : IQuery<SalesOrderDetailsDto?>;
public sealed record SalesOrderDetailsDto(long SalesOrderId, string CustomerName, string Status,
    decimal Total, IReadOnlyList<SalesOrderLineDto> Lines);

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
        return SalesOrderDetailsDto.From(header, lines);
    }
}
```

Query handlers depend on `IDbConnectionFactory`, not `ISalesOrderRepository` (a write-side concept).
Keeping them separate stops the repository growing thirty read methods.

## Step 4 — Read stored procedures (`sales` schema)

```sql
CREATE PROCEDURE sales.usp_SalesOrders_Search
    @CustomerId BIGINT = NULL, @Status INT = NULL, @Page INT, @PageSize INT
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

The `Total` here is a **read-time display projection** — not split-brain. The *authoritative* total driving
submit/approval is the domain's computed `Total`. *Domain owns the decision total; SQL may re-project a
display total.* Note: this proc only joins within the `sales` schema — a read that needs Inventory data asks
Inventory (contract/read endpoint), never a cross-schema join.

## Step 5 — What full CQRS would add (and why we don't)

Separate read store + projection pipeline; event sourcing; eventual consistency. None justified here —
architecture astronautics. CQRS-lite gives "features easy to locate, reads shaped for display" without the
tax.

## What to produce

- `GetSalesOrder`, `SearchSalesOrders` slices (two files each) + DTOs + read procs.
- `PagedResult<T>` in `Shared.Common`.
- `GET /sales-orders/{id}`, `GET /sales-orders?...` Carter endpoints.

**NOT:** domain on the read path; write logic; cross-schema joins; caching.

## Pitfalls

- Routing queries through the repository.
- Reusing write records for reads.
- Joining into another module's schema.
- Over-reaching into full CQRS.
- Business logic in read procs.

---

## Checkpoint

1. Why does a query handler skip the domain and the repository?
2. Domain `Total` vs. search-proc `Total` — why not split-brain?
3. What CQRS-lite gives you, and two things full CQRS adds.
4. A grid needs Inventory data — why not join `inventory.*`, and what instead?
