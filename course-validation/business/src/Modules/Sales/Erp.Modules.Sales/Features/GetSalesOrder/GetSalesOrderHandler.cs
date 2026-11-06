using Dapper;
using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;
using Mediator;

namespace Erp.Modules.Sales.Features;

internal sealed record GetSalesOrderQuery(long OrderId) : IQuery<SalesOrderDetailDto?>;

internal sealed record SalesOrderDetailDto(
    long Id, string OrderNumber, long CustomerId, string CustomerName, string Status, decimal Total,
    long CreatedBy, DateTime CreatedUtc, DateTime? SubmittedUtc, long? ApprovedBy, DateTime? ApprovedUtc,
    long? RejectedBy, string? RejectionReason, DateTime? ShippedUtc, decimal GrossTotal, SpecialDiscountDto? SpecialDiscount,
    IReadOnlyList<SalesOrderLineDto> Lines);

internal sealed record SpecialDiscountDto(string Program, string IdLabel, string HolderName, string IdNumber, decimal Rate,
    decimal VatRateRemoved, decimal VatExemption, decimal Discount);

internal sealed record SalesOrderLineDto(long ItemId, string ItemName, string Unit, decimal Quantity, decimal UnitPrice, decimal LineTotal,
    bool SpecialDiscountEligible);

internal sealed record SalesOrderHeaderRow(
    long Id, string OrderNumber, long CustomerId, string CustomerName, int Status, decimal Total, long CreatedBy,
    DateTime CreatedUtc, DateTime? SubmittedUtc, long? ApprovedBy, DateTime? ApprovedUtc, long? RejectedBy,
    string? RejectionReason, DateTime? ShippedUtc, decimal GrossTotal, string? SpecialDiscountProgram,
    string? SpecialDiscountIdLabel, string? SpecialDiscountHolderName, string? SpecialDiscountIdNumber,
    decimal? SpecialDiscountRate, decimal? SpecialDiscountVatRateRemoved, decimal SpecialDiscountVatExemption,
    decimal SpecialDiscountAmount);

internal sealed record SalesOrderLineRow(long ItemId, string ItemName, string Unit, decimal Quantity, decimal UnitPrice,
    bool SpecialDiscountEligible);

internal sealed class GetSalesOrderHandler(IDbConnectionFactory connections, ICurrentUser currentUser)
    : IQueryHandler<GetSalesOrderQuery, SalesOrderDetailDto?>
{
    public async ValueTask<SalesOrderDetailDto?> Handle(GetSalesOrderQuery q, CancellationToken ct)
    {
        var createdBy = SalesOrder.CreatorFilterFor(new UserId(currentUser.UserId), currentUser.Role)?.Value;

        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM sales.get_sales_order(@OrderId, @CreatedBy);
            SELECT * FROM sales.get_sales_order_lines(@OrderId);
            """, new { q.OrderId, CreatedBy = createdBy }, cancellationToken: ct));

        var h = await multi.ReadSingleOrDefaultAsync<SalesOrderHeaderRow>();
        if (h is null) return null;
        var lines = (await multi.ReadAsync<SalesOrderLineRow>())
            .Select(l => new SalesOrderLineDto(l.ItemId, l.ItemName, l.Unit, l.Quantity, l.UnitPrice,
                Math.Round(l.Quantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero), l.SpecialDiscountEligible))
            .ToList();

        return new SalesOrderDetailDto(
            h.Id, h.OrderNumber, h.CustomerId, h.CustomerName, ((SalesOrderStatus)h.Status).ToString(), h.Total,
            h.CreatedBy, h.CreatedUtc, h.SubmittedUtc, h.ApprovedBy, h.ApprovedUtc, h.RejectedBy, h.RejectionReason,
            h.ShippedUtc, h.GrossTotal,
            h.SpecialDiscountProgram is null ? null : new SpecialDiscountDto(h.SpecialDiscountProgram, h.SpecialDiscountIdLabel!,
                h.SpecialDiscountHolderName!, h.SpecialDiscountIdNumber!, h.SpecialDiscountRate ?? 0, h.SpecialDiscountVatRateRemoved ?? 0,
                h.SpecialDiscountVatExemption, h.SpecialDiscountAmount),
            lines);
    }
}
