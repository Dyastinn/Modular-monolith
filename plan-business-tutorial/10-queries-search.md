# Phase 10 — Queries: Get One & Search With Paging (CQRS-lite Read Side)

> Self-contained: the read side is explained fully here, starting from *why* reads are shaped differently
> from writes.

---

> ⏱️ **Time for this phase:** ~1–1.5 hr. Two read endpoints plus the search proc. The read side is
> deliberately simpler than the write side — notice how much *less* machinery a query needs.

## 1. What we're building

Two read endpoints:

- `GET /sales-orders/{id}` — full detail of one order (header + lines + names), for a detail screen.
- `GET /sales-orders?customerId=&status=&page=&pageSize=` — a filtered, paged list, for a search grid.

Both **skip the domain entirely** and read straight into flat DTOs via Dapper — the read side of
CQRS-lite.

## 2. Why it's designed this way

Reading changes nothing, so there are no invariants to protect — meaning the rich `SalesOrder` aggregate
is *unnecessary machinery* on the read path. A grid wants a flat, joined, screen-shaped row
(`OrderNumber, CustomerName, Status, Total, CreatedUtc`), which rarely matches the aggregate's shape.
Rebuilding aggregates just to flatten them is wasted work and couples your screens to your write model.

```
Write side: Command → Handler → SalesOrder aggregate → Repository → proc (write)
Read side:  Query   → Handler → Dapper → read proc → DTO           (no domain, no repository)
```

> ❓ **"Isn't the search proc's `Total` the same split-brain danger we avoided in the domain?"** No, and the
> difference is worth pinning down. The domain's computed total *decides* things — whether the customer is
> over their credit limit, whether the order needs approval. The read total merely *displays*. Two values
> that mean the same number but do different jobs: one is the authority, one is a convenience. The danger
> would be letting a decision depend on the displayed one.

### Queries just open a connection; commands add a transaction — and why

You'll notice every query handler here injects **`IDbConnectionFactory`** and just calls `factory.Create()`,
reads, and releases — while the command handlers in Phases 6–9 inject the *same* factory but then open a
**transaction** (`conn.BeginTransaction()`) around their writes. That's a deliberate, consistent split (the
rule was introduced in [Phase 6 §7](06-create-order.md)); here's the full reasoning, since it's the kind of
thing that looks inconsistent until you see *why*:

- **Why queries intentionally skip the transaction.** A query only *reads*. There's nothing to make
  atomic and nothing to roll back, so a transaction would add pure cost: it holds locks for the duration and
  can block writers, all to protect an operation that changes nothing. Grabbing a connection, running the
  read, and releasing it is simpler *and* friendlier to concurrency. Fewer moving parts, by design.
- **Why commands open a transaction.** A command usually makes *several* writes that must succeed or fail
  **together** (insert header + N lines; reserve stock + change status; update status + adjust balance). The
  transaction is what makes that all-or-nothing. A read has no such requirement, so it needs no such tool.
- **Is there any danger in "mixing" them?** Not in *using both* — that's exactly right, each for its job.
  The danger is using the **wrong** one: doing multi-step **writes** on a bare `factory.Create()` connection
  (no transaction → a mid-way failure leaves half-written data), or wrapping plain **reads** in a transaction
  for no reason (needless locking). Match the tool to the intent — read vs. atomic-write — and you're fine.
- **When *would* a query participate in a transaction?** Rarely, and always because it's part of a bigger
  *write* operation, not a standalone read. The classic case is a **read-modify-write** inside a command:
  you must read the current value *on the same transaction* you're about to write on, so you see a
  consistent snapshot and hold the row against concurrent writers (our version of that is the atomic
  `usp_Inventory_Reserve` in [Phase 7](07-submit-reserve-concurrency.md) — the "read" and the "write" are
  fused into one statement *inside* the transaction). A second, rarer case is a multi-statement **report**
  that must be internally consistent, where you'd open a read-only transaction (or snapshot isolation). But
  the standalone screen queries in *this* phase are none of those — so they stay transaction-free.

## 3. Get one order

`Modules/Sales/Features/GetSalesOrder/GetSalesOrderHandler.cs`:

```csharp
public sealed record GetSalesOrderQuery(long OrderId) : IQuery<SalesOrderDetailDto?>;

public sealed record SalesOrderDetailDto(
    long Id, string OrderNumber, long CustomerId, string CustomerName,
    string Status, decimal Total, DateTime CreatedUtc,
    IReadOnlyList<SalesOrderLineDto> Lines);

public sealed record SalesOrderLineDto(long ItemId, string ItemName, int Quantity, decimal UnitPrice, decimal LineTotal);

// The flat "row" shapes Dapper reads from the proc's two result sets — plain DB shapes, not the API DTO.
// (Status comes back as its stored int; the handler converts it to a readable name below.)
public sealed record SalesOrderHeaderRow(
    long Id, string OrderNumber, long CustomerId, string CustomerName, int Status, decimal Total, DateTime CreatedUtc);
public sealed record SalesOrderLineRow(long ItemId, string ItemName, int Quantity, decimal UnitPrice);

public sealed class GetSalesOrderHandler(IDbConnectionFactory factory)
    : IQueryHandler<GetSalesOrderQuery, SalesOrderDetailDto?>   // query marker (Phase 2)
{
    public async Task<SalesOrderDetailDto?> Handle(GetSalesOrderQuery q, CancellationToken ct)
    {
        using var conn = factory.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            "sales.usp_SalesOrder_GetDetail", new { q.OrderId },
            commandType: CommandType.StoredProcedure, cancellationToken: ct));

        var header = await multi.ReadSingleOrDefaultAsync<SalesOrderHeaderRow>();
        if (header is null) return null;
        var lines = (await multi.ReadAsync<SalesOrderLineRow>()).ToList();

        return new SalesOrderDetailDto(
            header.Id, header.OrderNumber, header.CustomerId, header.CustomerName,
            ((SalesOrderStatus)header.Status).ToString(), header.Total, header.CreatedUtc,
            lines.Select(l => new SalesOrderLineDto(l.ItemId, l.ItemName, l.Quantity, l.UnitPrice, l.Quantity * l.UnitPrice)).ToList());
    }
}
```

The handler depends on `IDbConnectionFactory`, **not** `ISalesOrderRepository`. The repository is a
write-side tool (it deals in aggregates); reads don't need it.

## 4. Search with filters and paging

`Modules/Sales/Features/SearchSalesOrders/SearchSalesOrdersHandler.cs`:

```csharp
public sealed record SearchSalesOrdersQuery(
    long? CustomerId, string? Status, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<SalesOrderSummaryDto>>;

public sealed record SalesOrderSummaryDto(
    long Id, string OrderNumber, string CustomerName, string Status, decimal Total, DateTime CreatedUtc);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed class SearchSalesOrdersValidator : AbstractValidator<SearchSalesOrdersQuery>
{
    public SearchSalesOrdersValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);   // cap page size — never let a client ask for a million rows
    }
}

public sealed class SearchSalesOrdersHandler(IDbConnectionFactory factory)
    : IQueryHandler<SearchSalesOrdersQuery, PagedResult<SalesOrderSummaryDto>>   // query marker (Phase 2)
{
    public async Task<PagedResult<SalesOrderSummaryDto>> Handle(SearchSalesOrdersQuery q, CancellationToken ct)
    {
        // Translate a status name to its number for the proc (null = no filter).
        // TryParse, not Parse: an unknown ?status=Foo is bad *input*, so throw a ValidationException
        // (→ 400 via the global handler, Phase 11), never an unhandled 500.
        int? statusNum = null;
        if (q.Status is not null)
        {
            if (!Enum.TryParse<SalesOrderStatus>(q.Status, ignoreCase: true, out var parsed))
                throw new ValidationException($"Unknown status '{q.Status}'.");
            statusNum = (int)parsed;
        }

        using var conn = factory.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            "sales.usp_SalesOrders_Search",
            new { q.CustomerId, Status = statusNum, q.Page, q.PageSize },
            commandType: CommandType.StoredProcedure, cancellationToken: ct));

        var items = (await multi.ReadAsync<SalesOrderSummaryDto>()).ToList();
        var total = await multi.ReadSingleAsync<int>();   // second result set: total row count
        return new PagedResult<SalesOrderSummaryDto>(items, q.Page, q.PageSize, total);
    }
}
```

## 5. The search stored procedure

```sql
CREATE OR ALTER PROCEDURE sales.usp_SalesOrders_Search
    @CustomerId BIGINT = NULL,
    @Status     INT    = NULL,
    @Page       INT,
    @PageSize   INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Page of results
    SELECT o.Id, o.OrderNumber, c.Name AS CustomerName, o.Status, o.TotalAmount AS Total, o.CreatedUtc
    FROM sales.SalesOrder o
    JOIN sales.Customer c ON c.Id = o.CustomerId
    WHERE (@CustomerId IS NULL OR o.CustomerId = @CustomerId)
      AND (@Status     IS NULL OR o.Status     = @Status)
    ORDER BY o.CreatedUtc DESC
    OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;

    -- Total count (for paging UI)
    SELECT COUNT(*)
    FROM sales.SalesOrder o
    WHERE (@CustomerId IS NULL OR o.CustomerId = @CustomerId)
      AND (@Status     IS NULL OR o.Status     = @Status);
END
```

> ❓ **You might be wondering: "Why two queries — the page *and* a count?"** The grid needs both "here
> are 20 rows" and "there are 137 total, so 7 pages." `OFFSET/FETCH` returns the page; `COUNT(*)` returns
> the total. Returning both as two result sets in one proc call is one round-trip. The
> `(@X IS NULL OR col = @X)` trick makes each filter optional: pass `NULL` and that condition disappears.

> 💡 **Status as a *string* in the API, a *number* in the DB.** The API accepts `?status=Approved`
> (readable for clients) and the handler converts it to `2` for the proc. Never leak raw enum numbers
> across your API boundary — they're an internal storage detail.

## 6. The endpoints (each a Carter module in its feature folder)

```csharp
// Modules/Sales/Features/GetSalesOrder/GetSalesOrderEndpoint.cs
public sealed class GetSalesOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet("/sales-orders/{id:long}",
            async (long id, ISender sender, CancellationToken ct) =>
            {
                var dto = await sender.Send(new GetSalesOrderQuery(id), ct);
                return dto is null ? Results.NotFound() : Results.Ok(dto);
            })
            .RequireAuthorization();   // any logged-in user
}

// Modules/Sales/Features/SearchSalesOrders/SearchSalesOrdersEndpoint.cs
public sealed class SearchSalesOrdersEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet("/sales-orders",
            async (long? customerId, string? status, int page, int pageSize, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(
                    new SearchSalesOrdersQuery(customerId, status, page == 0 ? 1 : page, pageSize == 0 ? 20 : pageSize), ct);
                return Results.Ok(result);
            })
            .RequireAuthorization();
}
```

`.RequireAuthorization()` with no role means "any authenticated user" — reads aren't role-gated here,
but they *do* require a valid token.

> 🔒 **Row-level authorization (a real ERP gap to be aware of).** As written, *any* logged-in user can read
> *every* order. In a real ERP a SalesRep usually sees only their own customers' orders, while a Manager
> sees all. That's a **data-dependent** rule (it depends on *who* is asking and *what* they're asking for),
> so — per [Phase 4 §9](04b-jwt-login.md) — it belongs in the handler/query, not an endpoint attribute: pass
> `ICurrentUser.UserId`/`Role` into the query and add a `CreatedByUserId = @CallerId` filter for
> non-managers. We keep it open here to stay focused on CQRS-lite shape, but flag it as the first thing
> you'd add for production. ([Phase 13](13-wrap-up-checklist.md) lists it.)

> ⚡ **Paging at scale: `OFFSET/FETCH` vs. keyset.** `OFFSET (@Page-1)*@PageSize` is perfect for a few
> hundred pages, but it gets slower the *deeper* you page — the database still scans and discards all the
> skipped rows. For very large tables the production-grade answer is **keyset (a.k.a. "seek") pagination**:
> instead of "skip 10,000 rows," you say "give me the next 20 *after* this `CreatedUtc`/`Id`." It needs a
> stable sort key and a cursor instead of a page number. Overkill for this tutorial's data volumes — but
> know the term, because a slow "page 500" is a real complaint you'll eventually field.

## 7. How a search request flows

```
GET /sales-orders?status=PendingApproval&page=1&pageSize=20   (valid token)
1. Auth ok.
2. Endpoint builds SearchSalesOrdersQuery(null, "PendingApproval", 1, 20).
3. (Once wired in Phase 11) ValidationBehavior: page 1 > 0 ✓, pageSize 20 in [1,100] ✓.
4. Handler converts "PendingApproval" → 1, calls sales.usp_SalesOrders_Search.
5. Proc returns 20 rows + a total count (say 43).
6. Handler wraps them in PagedResult { Items, Page=1, PageSize=20, TotalCount=43, TotalPages=3 }.
7. 200 OK with the JSON.
```

No domain object was built anywhere in that flow — just SQL → DTO. Compare the effort to the create
flow; the asymmetry is the whole point of CQRS-lite. (The `ValidationBehavior` and the global handler that
turns an unknown `?status=` into a clean `400` are wired in [Phase 11](11-cross-cutting.md); until then the
query still runs, it just won't pre-validate paging inputs automatically.)

## 7b. Checkpoint — read one and search ✅

With some orders created (Phases 6–8), fetch one and list them (any valid token works):

```bash
curl http://localhost:5xxx/sales-orders/{id} -H "Authorization: Bearer $TOKEN"                       # → 200 detail JSON
curl "http://localhost:5xxx/sales-orders?status=PendingApproval&page=1&pageSize=20" -H "Authorization: Bearer $TOKEN"
# → 200 { items: [...], page: 1, pageSize: 20, totalCount: N, totalPages: ... }
```

If the detail endpoint returns one order with its lines and names, and the search returns a paged envelope
that respects `?status=` / `?customerId=` filters, **Phase 10 is done and verifiable** — and notice how much
less machinery a read needed than a write.

## 8. Why each component exists

| Component | Why |
|---|---|
| `GetSalesOrderQuery` / handler | Read one order into a screen-shaped detail DTO, no domain. |
| `SearchSalesOrdersQuery` / handler | Filtered, paged list for a grid. |
| `PagedResult<T>` | Standard envelope carrying items + paging metadata. |
| Read DTOs (`...Dto`, `...Row`) | Shaped for the screen; evolve independently of the write model. |
| `sales.usp_SalesOrders_Search` | Set-based join/filter/page — where SQL genuinely shines. |
| Validator on the query | Guards paging inputs (positive page, capped page size). |

## 9. Common mistakes & best practices

- ❌ **Routing reads through `ISalesOrderRepository`.** ✅ Go straight to Dapper; reads need no
  aggregate.
- ❌ **Reusing write-side records as read DTOs.** ✅ Separate read models; screens change on their own
  schedule.
- ❌ **Unbounded page size** (client asks for 1,000,000 rows). ✅ Cap it in the validator (≤ 100).
- ❌ **Leaking enum numbers across the API.** ✅ Accept/return status *names*; convert at the boundary.
- ❌ **Two separate round-trips for page + count.** ✅ One proc, two result sets.
- ✅ **Best practice: put business decisions nowhere near read procs.** A read may project/aggregate for
  display, but must never change state or enforce policy.

---

## ✅ Check Yourself

1. Why does the query handler depend on `IDbConnectionFactory` and not the repository?
2. Why does the search proc return *two* result sets?
3. Why does the API speak status *names* while the database stores *numbers*?
4. Why cap `PageSize`, and where is that enforced?

> 👉 Next: [Phase 11 — Cross-cutting concerns: validation, error handling, logging](11-cross-cutting.md).
