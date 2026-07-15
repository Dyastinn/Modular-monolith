using System.Data;
using Dapper;
using Erp.Shared.Common.Data;
using Erp.Modules.Sales.Domain;
using Erp.Shared.Messaging;

namespace Erp.Modules.Sales.Features;

public sealed record GetSalesOrderQuery(long OrderId) : IQuery<SalesOrderDetailDto?>;

public sealed record SalesOrderDetailDto(
    long Id, string OrderNumber, long CustomerId, string CustomerName,
    string Status, decimal Total, DateTime CreatedUtc,
    IReadOnlyList<SalesOrderLineDto> Lines);

public sealed record SalesOrderLineDto(long ItemId, string ItemName, int Quantity, decimal UnitPrice, decimal LineTotal);

// Flat "row" shapes Dapper reads from the proc's two result sets.
public sealed record SalesOrderHeaderRow(
    long Id, string OrderNumber, long CustomerId, string CustomerName, int Status, decimal Total, DateTime CreatedUtc);
public sealed record SalesOrderLineRow(long ItemId, string ItemName, int Quantity, decimal UnitPrice);

public sealed class GetSalesOrderHandler(IDbConnectionFactory factory)
    : IQueryHandler<GetSalesOrderQuery, SalesOrderDetailDto?>
{
    public async Task<SalesOrderDetailDto?> Handle(GetSalesOrderQuery q, CancellationToken ct)
    {
        using var conn = factory.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            "sales.usp_SalesOrder_GetDetail", new { q.OrderId },
            commandType: CommandType.StoredProcedure, cancellationToken: ct));

        var header = await multi.ReadSingleOrDefaultAsync<SalesOrderHeaderRow>();
        if (header is null) return null;
        var lines = (await multi.ReadAsync<SalesOrderLineRow>()).ToList();

        return new SalesOrderDetailDto(
            header.Id, header.OrderNumber, header.CustomerId, header.CustomerName,
            ((SalesOrderStatus)header.Status).ToString(), header.Total, header.CreatedUtc,
            lines.Select(l => new SalesOrderLineDto(l.ItemId, l.ItemName, l.Quantity, l.UnitPrice, l.Quantity * l.UnitPrice)).ToList());
    }
}
