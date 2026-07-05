using System.Data;
using Dapper;
using Erp.Modules.Sales.Domain;
using Erp.Shared.Common;
using Erp.Shared.Infrastructure.Persistence;

namespace Erp.Modules.Sales.Infrastructure;

public sealed class SalesOrderRepository(IDbConnectionFactory factory) : ISalesOrderRepository
{
    public async Task<SalesOrderId> AddAsync(SalesOrder order, IDbTransaction tx, CancellationToken ct)
    {
        var conn = tx.Connection!;   // reuse the handler's transaction's connection
        var orderId = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "sales.usp_SalesOrder_Insert",
            new { order.CustomerId, Status = (int)order.Status },
            transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));

        foreach (var line in order.Lines)
            await conn.ExecuteAsync(new CommandDefinition(
                "sales.usp_SalesOrderLine_Insert",
                new { SalesOrderId = orderId, line.ProductId, Quantity = line.Quantity.Value,
                      UnitPrice = line.UnitPrice.Amount },
                transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));

        return new SalesOrderId(orderId);   // hand the new id back to the handler
    }

    public async Task<SalesOrder?> GetAsync(SalesOrderId id, CancellationToken ct)
    {
        using var conn = factory.Create();
        var header = await conn.QuerySingleOrDefaultAsync(new CommandDefinition(
            "sales.usp_SalesOrder_Get", new { SalesOrderId = id.Value },
            commandType: CommandType.StoredProcedure, cancellationToken: ct));
        if (header is null) return null;

        var lineRows = await conn.QueryAsync(new CommandDefinition(
            "sales.usp_SalesOrderLine_GetByOrder", new { SalesOrderId = id.Value },
            commandType: CommandType.StoredProcedure, cancellationToken: ct));

        var lines = lineRows.Select(r => new SalesOrderLine(
            new ProductId((long)r.ProductId),
            new Quantity((int)r.Quantity),
            new Money((decimal)r.UnitPrice)));

        return SalesOrder.Rehydrate(
            new SalesOrderId((long)header.SalesOrderId),
            new CustomerId((long)header.CustomerId),
            (SalesOrderStatus)(int)header.Status,
            lines,
            (byte[])header.RowVersion);
    }

    public async Task UpdateAsync(SalesOrder order, IDbTransaction tx, CancellationToken ct)
    {
        var conn = tx.Connection!;
        var rows = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "sales.usp_SalesOrder_UpdateStatus",
            new { SalesOrderId = order.Id.Value, Status = (int)order.Status, RowVersion = order.RowVersion },
            transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));
        if (rows == 0)
            throw new ConcurrencyException($"Sales order {order.Id.Value} was modified by someone else.");
    }
}

public sealed class CustomerRepository(IDbConnectionFactory factory) : ICustomerRepository
{
    public async Task<Customer?> GetAsync(CustomerId id, CancellationToken ct)
    {
        using var conn = factory.Create();
        var row = await conn.QuerySingleOrDefaultAsync(new CommandDefinition(
            "sales.usp_Customer_Get", new { CustomerId = id.Value },
            commandType: CommandType.StoredProcedure, cancellationToken: ct));
        if (row is null) return null;
        return Customer.Rehydrate(new CustomerId((long)row.CustomerId), (string)row.Name,
            (bool)row.IsActive, (bool)row.OnCreditHold);
    }
}

// Phase 4 §File1: "a stub that returns a fixed price is fine to get this running."
public sealed class PricingService(IDbConnectionFactory factory) : IPricingService
{
    public async Task<Money> GetUnitPrice(ProductId product, CancellationToken ct)
    {
        using var conn = factory.Create();
        var price = await conn.ExecuteScalarAsync<decimal?>(new CommandDefinition(
            "sales.usp_Product_GetPrice", new { ProductId = product.Value },
            commandType: CommandType.StoredProcedure, cancellationToken: ct));
        return new Money(price ?? 0m);
    }
}
