using System.Data.Common;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Inventory.Contracts;

// Every call that takes a DbTransaction joins the caller's transaction, so stock and the caller's own change commit
// or roll back together. The caller must be on the request's ISharedConnection (every module DbContext is).
public interface IStockReservation
{
    // false = not enough available stock; nothing changed.
    Task<bool> TryReserveAsync(long itemId, decimal quantity, DbTransaction tx, CancellationToken ct);
    Task ReleaseAsync(long itemId, decimal quantity, DbTransaction tx, CancellationToken ct);
}

public interface IStockReceiving
{
    Task ReceiveAsync(long itemId, decimal quantity, string reference, long userId, DbTransaction tx, CancellationToken ct);
}

public sealed record CatalogItem(long Id, string Sku, string Name, string Unit, bool AllowsFractions, bool IsActive);

public interface IItemCatalog
{
    Task<IReadOnlyList<CatalogItem>> GetAsync(IReadOnlyCollection<long> itemIds, CancellationToken ct);
}

// Integration events: public, because other modules handle them. Raised through the Inventory outbox.
public sealed record ItemCatalogChanged(long ItemId, string Sku, string Name, string Unit, bool AllowsFractions, bool IsActive)
    : IDomainEvent;

public sealed record ShipmentRecorded(long ShipmentId, string ShipmentNumber, long SalesOrderId, DateTime ShippedUtc)
    : IDomainEvent;
