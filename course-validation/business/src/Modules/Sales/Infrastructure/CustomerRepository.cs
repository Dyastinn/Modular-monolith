using System.Data;
using Dapper;
using Erp.Shared.Common.Data;
using Erp.Modules.Sales.Domain;

namespace Erp.Modules.Sales.Infrastructure;

public sealed class CustomerRepository(IDbConnectionFactory factory) : ICustomerRepository
{
    private sealed record CustomerRow(long Id, string Name, bool IsBlocked, decimal CreditLimit, decimal OutstandingBalance);

    public async Task<Customer?> GetAsync(CustomerId id, CancellationToken ct)
    {
        using var conn = factory.Create();
        var row = await conn.QuerySingleOrDefaultAsync<CustomerRow>(new CommandDefinition(
            "sales.usp_Customer_GetById", new { Id = id.Value },
            commandType: CommandType.StoredProcedure, cancellationToken: ct));
        if (row is null) return null;

        return new Customer(new CustomerId(row.Id), row.Name, row.IsBlocked,
            new Money(row.CreditLimit), new Money(row.OutstandingBalance));
    }

    public async Task AdjustBalanceAsync(CustomerId id, decimal delta, IDbTransaction tx, CancellationToken ct)
    {
        await tx.Connection!.ExecuteAsync(new CommandDefinition(
            "sales.usp_Customer_AdjustBalance",
            new { CustomerId = id.Value, Delta = delta },
            transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }
}
