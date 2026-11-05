using System.Data.Common;

namespace Erp.Modules.Inventory.Contracts;

// Both calls join the caller's transaction, so stock and the caller's own change commit or roll back together.
// The caller must be on the request's ISharedConnection (every module DbContext is).
public interface IStockReservation
{
    // false = not enough available stock; nothing changed.
    Task<bool> TryReserveAsync(long itemId, int quantity, DbTransaction tx, CancellationToken ct);
    Task ReleaseAsync(long itemId, int quantity, DbTransaction tx, CancellationToken ct);
}
