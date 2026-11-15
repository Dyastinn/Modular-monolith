using Dapper;
using Erp.Modules.Purchasing.Domain;
using Erp.Shared.BuildingBlocks;
using FluentValidation;

namespace Erp.Modules.Purchasing.Features;

// AwaitingMe: purchase orders waiting for the caller's approval (never their own).
internal sealed record SearchPurchaseOrdersQuery(long? SupplierId = null, string? Status = null, string? Search = null,
    bool AwaitingMe = false, string? Sort = null, bool Desc = false, int Page = 1, int PageSize = 20);

internal sealed record PurchaseOrderSummaryDto(long Id, string Number, long SupplierId, string SupplierName, string Status,
    decimal Total, DateOnly? ExpectedDate, long CreatedBy, DateTime CreatedUtc, DateTime? SubmittedUtc);

internal sealed record PurchaseOrderSummaryRow(long Id, string Number, long SupplierId, string SupplierName, int Status,
    decimal Total, DateOnly? ExpectedDate, long CreatedBy, DateTime CreatedUtc, DateTime? SubmittedUtc);

internal sealed class SearchPurchaseOrdersValidator : AbstractValidator<SearchPurchaseOrdersQuery>
{
    public SearchPurchaseOrdersValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
        RuleFor(x => x.Sort).ValidSort("number", "supplierName", "status", "total", "expectedDate", "submittedUtc", "createdUtc");
        RuleFor(x => x.Status).Must(s => s is null || Enum.GetNames<PurchaseOrderStatus>().Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage(x => $"Unknown status '{x.Status}'.");
    }
}

internal sealed class SearchPurchaseOrdersHandler(IDbConnectionFactory connections, ICurrentUser currentUser)
{
    public async Task<PagedResult<PurchaseOrderSummaryDto>> HandleAsync(SearchPurchaseOrdersQuery q, CancellationToken ct)
    {
        int? status = q.Status is null ? null : (int)Enum.Parse<PurchaseOrderStatus>(q.Status, true);
        var args = new
        {
            q.SupplierId, Status = status, Search = SearchText.ForLike(q.Search),
            Awaiting = q.AwaitingMe ? (int?)(PurchasingRoles.Approvers.Contains(currentUser.Role) ? 1 : 0) : null,
            ExcludeCreator = q.AwaitingMe ? (long?)currentUser.UserId : null,
            q.Sort, q.Desc, q.Page, q.PageSize
        };

        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM purchasing.search_purchase_orders(@SupplierId, @Status, @Search, @Awaiting, @ExcludeCreator, @Sort, @Desc, @Page, @PageSize);
            SELECT purchasing.count_purchase_orders(@SupplierId, @Status, @Search, @Awaiting, @ExcludeCreator);
            """, args, cancellationToken: ct));

        var rows = (await multi.ReadAsync<PurchaseOrderSummaryRow>())
            .Select(r => new PurchaseOrderSummaryDto(r.Id, r.Number, r.SupplierId, r.SupplierName,
                ((PurchaseOrderStatus)r.Status).ToString(), r.Total, r.ExpectedDate, r.CreatedBy, r.CreatedUtc, r.SubmittedUtc))
            .ToList();
        return new PagedResult<PurchaseOrderSummaryDto>(rows, q.Page, q.PageSize, (int)await multi.ReadSingleAsync<long>());
    }
}

internal sealed record GetPurchaseOrderQuery(long PurchaseOrderId);

internal sealed record PurchaseOrderDetailDto(long Id, string Number, long SupplierId, string SupplierName, string Status,
    decimal Total, DateOnly? ExpectedDate, string? Notes, long CreatedBy, DateTime CreatedUtc, long? ApprovedBy,
    DateTime? ApprovedUtc, DateTime? SentUtc, IReadOnlyList<PurchaseOrderLineDto> Lines, DateTime? SubmittedUtc, long? RejectedBy,
    DateTime? RejectedUtc, string? RejectionReason);

internal sealed record PurchaseOrderLineDto(long Id, long ItemId, string Sku, string ItemName, string Unit, decimal Quantity,
    decimal ReceivedQuantity, decimal Outstanding, decimal UnitCost, decimal LineTotal);

internal sealed record PurchaseOrderHeaderRow(long Id, string Number, long SupplierId, string SupplierName, int Status,
    decimal Total, DateOnly? ExpectedDate, string? Notes, long CreatedBy, DateTime CreatedUtc, long? ApprovedBy,
    DateTime? ApprovedUtc, DateTime? SentUtc, DateTime? SubmittedUtc, long? RejectedBy, DateTime? RejectedUtc, string? RejectionReason);

internal sealed record PurchaseOrderLineRow(long Id, long ItemId, string Sku, string ItemName, string Unit, decimal Quantity,
    decimal ReceivedQuantity, decimal UnitCost);

internal sealed class GetPurchaseOrderHandler(IDbConnectionFactory connections)
{
    public async Task<PurchaseOrderDetailDto?> HandleAsync(GetPurchaseOrderQuery q, CancellationToken ct)
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
            h.Total, h.ExpectedDate, h.Notes, h.CreatedBy, h.CreatedUtc, h.ApprovedBy, h.ApprovedUtc, h.SentUtc, lines,
            h.SubmittedUtc, h.RejectedBy, h.RejectedUtc, h.RejectionReason);
    }
}
