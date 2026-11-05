using Dapper;
using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;
using Mediator;

namespace Erp.Modules.Sales.Features;

internal sealed record GetSalesOrderQuery(long OrderId) : IQuery<SalesOrderDetailDto?>;

internal sealed record SalesOrderDetailDto(
    long Id, string OrderNumber, long CustomerId, string CustomerName,
    string Status, decimal Total, DateTime CreatedUtc,
    IReadOnlyList<SalesOrderLineDto> Lines);

internal sealed record SalesOrderLineDto(long ItemId, string ItemName, int Quantity, decimal UnitPrice, decimal LineTotal);

// Flat rows, exactly as the SQL functions return them (snake_case columns map onto these names).
internal sealed record SalesOrderHeaderRow(
    long Id, string OrderNumber, long CustomerId, string CustomerName, int Status, decimal Total, DateTime CreatedUtc);
internal sealed record SalesOrderLineRow(long ItemId, string ItemName, int Quantity, decimal UnitPrice);

// A read: no aggregate, no change tracking — Dapper maps the two functions' rows straight into DTOs.
internal sealed class GetSalesOrderHandler(IDbConnectionFactory connections)
    : IQueryHandler<GetSalesOrderQuery, SalesOrderDetailDto?>
{
    public async ValueTask<SalesOrderDetailDto?> Handle(GetSalesOrderQuery q, CancellationToken ct)
    {
        await using var conn = connections.Create();
        // Two statements, one round trip, two result sets.
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            "SELECT * FROM sales.get_sales_order(@OrderId); SELECT * FROM sales.get_sales_order_lines(@OrderId);",
            new { q.OrderId }, cancellationToken: ct));

        var header = await multi.ReadSingleOrDefaultAsync<SalesOrderHeaderRow>();
        if (header is null) return null;
        var lines = (await multi.ReadAsync<SalesOrderLineRow>()).ToList();

        return new SalesOrderDetailDto(
            header.Id, header.OrderNumber, header.CustomerId, header.CustomerName,
            ((SalesOrderStatus)header.Status).ToString(), header.Total, header.CreatedUtc,
            lines.Select(l => new SalesOrderLineDto(l.ItemId, l.ItemName, l.Quantity, l.UnitPrice, l.Quantity * l.UnitPrice)).ToList());
    }
}
