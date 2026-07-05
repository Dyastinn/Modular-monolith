using System.Data;

namespace Erp.Modules.Sales.Domain;

// The module declares the data-access "ports" it needs; Infrastructure implements them (Phase 6 §4).

public interface ISalesOrderRepository
{
    Task<string> NextOrderNumberAsync(CancellationToken ct);
    Task<long> AddAsync(SalesOrder order, IDbTransaction tx, CancellationToken ct);   // returns the DB-assigned id
    Task<SalesOrder?> GetAsync(SalesOrderId id, CancellationToken ct);
    Task UpdateStatusAsync(SalesOrder order, IDbTransaction tx, CancellationToken ct);
    Task ReplaceLinesAsync(SalesOrder order, IDbTransaction tx, CancellationToken ct);  // Phase 9
}

public interface ICustomerRepository
{
    Task<Customer?> GetAsync(CustomerId id, CancellationToken ct);
    // delta is a SIGNED decimal, not Money: approval adds (+), cancelling an approved order reverses (−).
    Task AdjustBalanceAsync(CustomerId id, decimal delta, IDbTransaction tx, CancellationToken ct);
}

public interface IItemRepository
{
    // Loads the requested items WITH their category rules, in one round-trip.
    Task<IReadOnlyList<ItemForOrder>> GetForOrderAsync(IReadOnlyList<long> itemIds, CancellationToken ct);
}

// A small read shape the handler needs: item + its price + its category rules.
public sealed record ItemForOrder(long ItemId, bool IsActive, Money UnitPrice, ItemCategory Category);
