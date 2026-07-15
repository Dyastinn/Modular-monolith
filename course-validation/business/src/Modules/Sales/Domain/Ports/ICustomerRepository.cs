using System.Data;

namespace Erp.Modules.Sales.Domain;

// A data-access "port" the module declares; Sales's own Infrastructure implements it (Phase 6 §4).
public interface ICustomerRepository
{
    Task<Customer?> GetAsync(CustomerId id, CancellationToken ct);
    // delta is a SIGNED decimal, not Money: approval adds (+), cancelling an approved order reverses (−).
    Task AdjustBalanceAsync(CustomerId id, decimal delta, IDbTransaction tx, CancellationToken ct);
}
