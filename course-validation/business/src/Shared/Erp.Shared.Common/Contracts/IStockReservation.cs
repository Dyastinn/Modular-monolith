using System.Data;

namespace Erp.Shared.Common;

// The ONLY way Sales touches stock (Phase 7 §3). Sales depends on this interface; the Inventory
// module implements it. Neither module references the other — the contract is the wall.
public interface IStockReservation
{
    // Returns true if the reservation succeeded (stock was available), false otherwise.
    Task<bool> TryReserveAsync(long itemId, int qty, IDbTransaction tx, CancellationToken ct);
    Task ReleaseAsync(long itemId, int qty, IDbTransaction tx, CancellationToken ct);
}
