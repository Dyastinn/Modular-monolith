using Dapper;
using Erp.Modules.Purchasing.Domain;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;

namespace Erp.Modules.Purchasing.Features;

internal sealed record SearchPurchaseOrdersQuery(long? SupplierId, string? Status, string? Search, int Page, int PageSize)
    : IQuery<PagedResult<PurchaseOrderSummaryDto>>;

internal sealed record PurchaseOrderSummaryDto(long Id, string Number, long SupplierId, string SupplierName, string Status,
    decimal Total, DateOnly? ExpectedDate, DateTime CreatedUtc);

internal sealed record PurchaseOrderSummaryRow(long Id, string Number, long SupplierId, string SupplierName, int Status,
    decimal Total, DateOnly? ExpectedDate, DateTime CreatedUtc);

internal sealed class SearchPurchaseOrdersValidator : AbstractValidator<SearchPurchaseOrdersQuery>
{
    public SearchPurchaseOrdersValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
        RuleFor(x => x.Status).Must(s => s is null || Enum.GetNames<PurchaseOrderStatus>().Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage(x => $"Unknown status '{x.Status}'.");
    }
}

internal sealed class SearchPurchaseOrdersHandler(IDbConnectionFactory connections)
    : IQueryHandler<SearchPurchaseOrdersQuery, PagedResult<PurchaseOrderSummaryDto>>
{
    public async ValueTask<PagedResult<PurchaseOrderSummaryDto>> Handle(SearchPurchaseOrdersQuery q, CancellationToken ct)
    {
        int? status = q.Status is null ? null : (int)Enum.Parse<PurchaseOrderStatus>(q.Status, true);
        var args = new { q.SupplierId, Status = status, Search = SearchText.ForLike(q.Search), q.Page, q.PageSize };

        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM purchasing.search_purchase_orders(@SupplierId, @Status, @Search, @Page, @PageSize);
            SELECT purchasing.count_purchase_orders(@SupplierId, @Status, @Search);
            """, args, cancellationToken: ct));

        var rows = (await multi.ReadAsync<PurchaseOrderSummaryRow>())
            .Select(r => new PurchaseOrderSummaryDto(r.Id, r.Number, r.SupplierId, r.SupplierName,
                ((PurchaseOrderStatus)r.Status).ToString(), r.Total, r.ExpectedDate, r.CreatedUtc))
            .ToList();
        return new PagedResult<PurchaseOrderSummaryDto>(rows, q.Page, q.PageSize, (int)await multi.ReadSingleAsync<long>());
    }
}

internal sealed record GetPurchaseOrderQuery(long PurchaseOrderId) : IQuery<PurchaseOrderDetailDto?>;

internal sealed record PurchaseOrderDetailDto(long Id, string Number, long SupplierId, string SupplierName, string Status,
    decimal Total, DateOnly? ExpectedDate, string? Notes, long CreatedBy, DateTime CreatedUtc, long? ApprovedBy,
    DateTime? ApprovedUtc, DateTime? SentUtc, IReadOnlyList<PurchaseOrderLineDto> Lines);

internal sealed record PurchaseOrderLineDto(long Id, long ItemId, string Sku, string ItemName, string Unit, decimal Quantity,
    decimal ReceivedQuantity, decimal Outstanding, decimal UnitCost, decimal LineTotal);

internal sealed record PurchaseOrderHeaderRow(long Id, string Number, long SupplierId, string SupplierName, int Status,
    decimal Total, DateOnly? ExpectedDate, string? Notes, long CreatedBy, DateTime CreatedUtc, long? ApprovedBy,
    DateTime? ApprovedUtc, DateTime? SentUtc);

internal sealed record PurchaseOrderLineRow(long Id, long ItemId, string Sku, string ItemName, string Unit, decimal Quantity,
    decimal ReceivedQuantity, decimal UnitCost);

internal sealed class GetPurchaseOrderHandler(IDbConnectionFactory connections) : IQueryHandler<GetPurchaseOrderQuery, PurchaseOrderDetailDto?>
{
    public async ValueTask<PurchaseOrderDetailDto?> Handle(GetPurchaseOrderQuery q, CancellationToken ct)
    {
        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM purchasing.get_purchase_order(@Id);
            SELECT * FROM purchasing.get_purchase_order_lines(@Id);
            """, new { Id = q.PurchaseOrderId }, cancellationToken: ct));

        var h = await multi.ReadSingleOrDefaultAsync<PurchaseOrderHeaderRow>();
        if (h is null) return null;
        var lines = (await multi.ReadAsync<PurchaseOrderLineRow>())
            .Select(l => new PurchaseOrderLineDto(l.Id, l.ItemId, l.Sku, l.ItemName, l.Unit, l.Quantity, l.ReceivedQuantity,
                l.Quantity - l.ReceivedQuantity, l.UnitCost, Math.Round(l.Quantity * l.UnitCost, 2, MidpointRounding.AwayFromZero)))
            .ToList();

        return new PurchaseOrderDetailDto(h.Id, h.Number, h.SupplierId, h.SupplierName, ((PurchaseOrderStatus)h.Status).ToString(),
            h.Total, h.ExpectedDate, h.Notes, h.CreatedBy, h.CreatedUtc, h.ApprovedBy, h.ApprovedUtc, h.SentUtc, lines);
    }
}
