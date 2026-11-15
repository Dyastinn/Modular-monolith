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
    // unitCost: what one unit cost us; the item's moving-average cost takes it in.
    Task ReceiveAsync(long itemId, decimal quantity, decimal unitCost, string reference, long userId, DbTransaction tx, CancellationToken ct);
}

public sealed record CatalogItem(long Id, string Sku, string Name, string Unit, bool AllowsFractions, bool IsActive);

public interface IItemCatalog
{
    Task<IReadOnlyList<CatalogItem>> GetAsync(IReadOnlyCollection<long> itemIds, CancellationToken ct);
}

// Integration events: public, because other modules handle them. Inventory publishes them from its own domain events,
// so they leave through its outbox and arrive at least once.
public sealed record ItemCatalogChanged(long ItemId, string Sku, string Name, string Unit, bool AllowsFractions, bool IsActive)
    : IIntegrationEvent;

// Lines: what left, and what each unit cost (the cost of goods sold). Empty only on events written before
// shipments carried lines (2026-11-15), which shipped the whole order.
public sealed record ShipmentRecorded(long ShipmentId, string ShipmentNumber, long SalesOrderId, DateTime ShippedUtc,
    IReadOnlyList<ShippedLine>? Lines = null) : IIntegrationEvent;

public sealed record ShippedLine(long ItemId, decimal Quantity, decimal UnitCost);
