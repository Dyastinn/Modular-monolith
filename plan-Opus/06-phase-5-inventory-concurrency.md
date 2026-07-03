# Phase 5 — Second Module: Inventory, Concurrency, Pricing, Approval, SubmitSalesOrder

**Goal:** add the **Inventory module**, make Sales reserve stock *across the module wall* through a
published contract, and implement reservation/deduction under concurrency, pricing, the approval rule, and
`SubmitSalesOrder`. Where "modules are islands" and "policy in the domain, atomicity in SQL" (Tension A/A.1)
become concrete.
**Produces:** the Inventory module, `IStockReservation` contract in Shared, `SubmitSalesOrder` slice, atomic
reservation/deduction procs, optimistic concurrency via `rowversion`, `PricingService`, approval flow.
**Prereq:** Phase 4 complete.

> The payoff phase. Spend time on §1 (the module boundary) and §3 (concurrency).

---

## Step 1 — Inventory is its own module; Sales reaches it through a contract

Stock is Inventory's data and responsibility → its own project (`Erp.Modules.Inventory`), own `inventory`
schema. Sales must not reference it or touch `inventory.*`. The only channel is a published contract in the
shared kernel:

```csharp
// Erp.Shared.Contracts/IStockReservation.cs
public interface IStockReservation
{
    Task<bool> TryReserveAsync(long productId, int quantity, IDbTransaction tx, CancellationToken ct);
    Task ReleaseAsync(long productId, int quantity, IDbTransaction tx, CancellationToken ct);
    Task DeductAsync(long productId, int quantity, IDbTransaction tx, CancellationToken ct);
}
```

Inventory implements it (`StockReservationService`); Sales calls it; the Api wires them. Neither module
references the other — Sales depends only on the interface. Put the contract in **Shared**, not Inventory,
so Sales can't see Inventory's internals. The DB transaction is shared for this atomic op — acceptable in a
one-database monolith; on a real split it becomes a saga (Phase 9).

## Step 2 — Pricing: the first *justified* domain service (inside Sales)

Pricing needs data beyond the `SalesOrder` (products/price lists) → a domain service. *A domain service
exists when logic spans aggregates or needs data the aggregate doesn't own.* If it were "unit price ×
quantity" over data already on the order, it'd be a method on `SalesOrderLine`, not a service.

```csharp
public interface IPricingService { Task<Money> GetUnitPrice(ProductId product, CancellationToken ct); }
```

Keep it honest: it computes prices; it doesn't submit orders or touch inventory.

## Step 3 — Inventory reservation: the concurrency lesson (inside Inventory)

### Naive (broken)

```csharp
var stock = await stockRepo.Get(productId);   // read
if (stock.Available >= qty) await stockRepo.Reserve(productId, qty);  // then write
```

Two requests for the last 5 both read 5, both pass, both reserve → 10 of 5. A **race condition**; domain
purity can't fix it because check and write aren't atomic.

### Correct — atomic guard in SQL

```sql
CREATE PROCEDURE inventory.usp_Stock_Reserve
    @ProductId BIGINT, @Qty INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE inventory.Stock
    SET Reserved = Reserved + @Qty
    WHERE ProductId = @ProductId AND (OnHand - Reserved) >= @Qty;   -- guard is part of the write
    SELECT @@ROWCOUNT;   -- 0 = insufficient, 1 = reserved
END
```

`@@ROWCOUNT = 0` → Sales' handler turns it into a domain-level failure. The domain *expresses* the rule
(validation, unit tests); the **authoritative** guard is this atomic SQL. Tension A.1:

> **Policy** ("reserve only what's available") is domain; **atomicity** ("no two callers win the last unit")
> is database. Both needed, different homes, *not* duplication.

## Step 4 — SubmitSalesOrder end-to-end (across the wall)

```csharp
public sealed record SubmitSalesOrderCommand(long SalesOrderId, bool ApprovalGranted) : ICommand<Unit>;

public sealed class SubmitSalesOrderHandler(
    ISalesOrderRepository orders, IStockReservation stock, IUnitOfWork uow)
    : ICommandHandler<SubmitSalesOrderCommand, Unit>
{
    public async Task<Unit> Handle(SubmitSalesOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetAsync(new SalesOrderId(cmd.SalesOrderId), ct)
            ?? throw new NotFoundException($"Sales order {cmd.SalesOrderId} not found.");

        order.Submit(cmd.ApprovalGranted);        // domain: legal? throws if not

        await using var tx = await uow.BeginAsync(ct);
        foreach (var line in order.Lines)         // Inventory (across the wall): atomic reservation
            if (!await stock.TryReserveAsync(line.ProductId.Value, line.Quantity.Value, tx.Inner, ct))
                throw new DomainException($"Insufficient stock for product {line.ProductId.Value}.");
        await orders.UpdateAsync(order, tx, ct);  // Submitted status + version check
        await tx.CommitAsync(ct);
        return Unit.Value;
    }
}
```

Four responsibilities, four homes: **Sales domain** decides legality (`Submit`), **Inventory** guarantees
safe reservation, the **contract** is the only channel, the **handler** owns the transaction boundary.
(`Unit` in `Shared.Messaging`.)

## Step 5 — Optimistic concurrency on the order

`rowversion` column + conditional update (`sales.usp_SalesOrder_UpdateStatus ... WHERE ... AND RowVersion =
@RowVersion; SELECT @@ROWCOUNT`). `0` → repository throws `ConcurrencyException`; handler lets it bubble.
Token = infrastructure; the *decision to fail on conflict* = domain-meaningful.

## Step 6 — Reserve / deduct / release (all Inventory's)

reserve-on-submit, deduct-on-complete, release-on-cancel. Each an atomic proc of the §3 shape. Write the
policy down.

## What to produce

- Inventory module (`Domain`, `inventory` schema + `Stock`, atomic `usp_Stock_Reserve`/`_Deduct`/`_Release`,
  `StockReservationService`); `IStockReservation` in Shared; `AddInventoryModule()`; Api calls both modules.
- `Unit`; `SubmitSalesOrderCommand` + validator + handler (two files); `POST /sales-orders/{id}/submit`.
- `IPricingService` + impl in Sales; wire into `CreateSalesOrder`.
- `rowversion` on `sales.SalesOrder`; `usp_SalesOrder_UpdateStatus`; `ConcurrencyException`.
- Unit tests (approval threshold, illegal submit states). Note the race integration test for Phase 8.

**NOT:** queries (Phase 6), cancel/complete full flow + events (Phase 7).

## Pitfalls

- **Sales reading/writing `inventory.*` directly.** Use the contract.
- **Check-then-act in C#** for stock. The guard must be atomic SQL.
- **Contract in the Inventory project.** Put it in Shared or Sales sees Inventory internals.
- **rowversion check in the domain.** Mechanism = infrastructure; outcome = domain-meaningful.
- **No release on cancel** → phantom shortages.

---

## Checkpoint

1. Why is Inventory a separate module, and how does Sales reserve without referencing it?
2. Why does the naive check fail, and what makes the SQL safe?
3. Is "reserve only if available" duplicated between domain and SQL? Why not?
4. Who owns the transaction boundary in `SubmitSalesOrder`, and why not the domain, repository, or
   Inventory?
5. Sharing the transaction across the contract — why acceptable now, and what changes on a real split?
