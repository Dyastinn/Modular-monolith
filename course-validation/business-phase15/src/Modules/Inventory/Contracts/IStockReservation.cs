using System.Data;

namespace Erp.Modules.Inventory.Contracts;

// Inventory's ONE public type: the contract Sales calls to reserve/release stock (introduced in Phase 7,
// relocated from Erp.Shared.Common into this module in Phase 15). Sales references this whole assembly but
// can see only this — everything else in Inventory is `internal`. The compiler is the wall now, not an
// absent project reference.
public interface IStockReservation
{
    // Returns true if the reservation succeeded (stock was available), false otherwise.
    Task<bool> TryReserveAsync(long itemId, int qty, IDbTransaction tx, CancellationToken ct);
    Task ReleaseAsync(long itemId, int qty, IDbTransaction tx, CancellationToken ct);
}
