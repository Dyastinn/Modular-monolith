using Dapper;
using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;
using Mediator;

namespace Erp.Modules.Sales.Features;

internal sealed record GetSalesOrderQuery(long OrderId) : IQuery<SalesOrderDetailDto?>;

internal sealed record SalesOrderDetailDto(
    long Id, string OrderNumber, long CustomerId, string CustomerName, string Status, decimal Total,
    DateTime CreatedUtc, DateTime? SubmittedUtc, long? ApprovedBy, DateTime? ApprovedUtc, string? RejectionReason,
    IReadOnlyList<SalesOrderLineDto> Lines);

internal sealed record SalesOrderLineDto(long ItemId, string ItemName, int Quantity, decimal UnitPrice, decimal LineTotal);

internal sealed record SalesOrderHeaderRow(
    long Id, string OrderNumber, long CustomerId, string CustomerName, int Status, decimal Total,
    DateTime CreatedUtc, DateTime? SubmittedUtc, long? ApprovedBy, DateTime? ApprovedUtc, string? RejectionReason);
internal sealed record SalesOrderLineRow(long ItemId, string ItemName, int Quantity, decimal UnitPrice);

internal sealed class GetSalesOrderHandler(IDbConnectionFactory connections)
    : IQueryHandler<GetSalesOrderQuery, SalesOrderDetailDto?>
{
    public async ValueTask<SalesOrderDetailDto?> Handle(GetSalesOrderQuery q, CancellationToken ct)
    {
        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            "SELECT * FROM sales.get_sales_order(@OrderId); SELECT * FROM sales.get_sales_order_lines(@OrderId);",
            new { q.OrderId }, cancellationToken: ct));

        var h = await multi.ReadSingleOrDefaultAsync<SalesOrderHeaderRow>();
        if (h is null) return null;
        var lines = (await multi.ReadAsync<SalesOrderLineRow>())
            .Select(l => new SalesOrderLineDto(l.ItemId, l.ItemName, l.Quantity, l.UnitPrice, l.Quantity * l.UnitPrice))
            .ToList();

        return new SalesOrderDetailDto(
            h.Id, h.OrderNumber, h.CustomerId, h.CustomerName, ((SalesOrderStatus)h.Status).ToString(), h.Total,
            h.CreatedUtc, h.SubmittedUtc, h.ApprovedBy, h.ApprovedUtc, h.RejectionReason, lines);
    }
}
