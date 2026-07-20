using System.Data;

namespace Erp.Modules.Sales.Domain;

// A data-access "port" the module declares; Sales's own Infrastructure implements it (Phase 6 §4).
internal interface ISalesOrderRepository
{
    Task<string> NextOrderNumberAsync(CancellationToken ct);
    Task<long> AddAsync(SalesOrder order, IDbTransaction tx, CancellationToken ct);   // returns the DB-assigned id
    Task<SalesOrder?> GetAsync(SalesOrderId id, CancellationToken ct);
    Task UpdateStatusAsync(SalesOrder order, IDbTransaction tx, CancellationToken ct);
    Task ReplaceLinesAsync(SalesOrder order, IDbTransaction tx, CancellationToken ct);  // Phase 9
}
