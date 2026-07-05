using System.Data;

namespace Erp.Shared.Common.Contracts;

public interface IStockReservation
{
    Task<bool> TryReserveAsync(long productId, int quantity, IDbTransaction tx, CancellationToken ct);  // hold (submit)
    Task ReleaseAsync(long productId, int quantity, IDbTransaction tx, CancellationToken ct);           // give back (cancel)
    Task DeductAsync(long productId, int quantity, IDbTransaction tx, CancellationToken ct);            // ship (complete)
}
