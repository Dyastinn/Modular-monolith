using System.Data;

namespace Erp.Modules.Sales.Domain;

// A read port (data access) the domain declares and Infrastructure implements. See Phase 4 §File1.
public interface IPricingService
{
    Task<Money> GetUnitPrice(ProductId product, CancellationToken ct);
}

public interface ICustomerRepository
{
    Task<Customer?> GetAsync(CustomerId id, CancellationToken ct);
}

public interface ISalesOrderRepository
{
    Task<SalesOrderId> AddAsync(SalesOrder order, IDbTransaction tx, CancellationToken ct);
    Task<SalesOrder?> GetAsync(SalesOrderId id, CancellationToken ct);
    Task UpdateAsync(SalesOrder order, IDbTransaction tx, CancellationToken ct);
}
