using System.Data.Common;

namespace Erp.Modules.Inventory.Contracts;

// What Sales may ask of Inventory. Both calls JOIN the caller's transaction (`tx`), so reserving stock and
// changing the order's status commit or roll back together. The caller must be on the request's shared
// connection (ISharedConnection) — every module DbContext is.
public interface IStockReservation
{
    // true = reserved; false = not enough available stock (nothing changed).
    Task<bool> TryReserveAsync(long itemId, int quantity, DbTransaction tx, CancellationToken ct);
    Task ReleaseAsync(long itemId, int quantity, DbTransaction tx, CancellationToken ct);
}
