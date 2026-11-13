using System.Data.Common;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Contracts;

public sealed record ShippableOrder(long OrderId, string OrderNumber, IReadOnlyList<ShippableLine> Lines);
public sealed record ShippableLine(long ItemId, decimal Quantity);

public sealed record OrderForInvoice(
    long OrderId, string OrderNumber, long CustomerId, string CustomerName, string? CustomerTin, string? CustomerAddress,
    int PaymentTermsDays, IReadOnlyList<InvoiceableLine> Lines, SpecialDiscount? SpecialDiscount);
public sealed record InvoiceableLine(long ItemId, string Description, string Unit, decimal Quantity, decimal UnitPrice, TaxType TaxType,
    bool SpecialDiscountEligible);

public interface ISalesOrders
{
    // Both join the caller's transaction. GetForShipment returns null when there is no such order, and throws a
    // DomainException unless the order is approved.
    Task<ShippableOrder?> GetForShipmentAsync(long orderId, DbTransaction tx, CancellationToken ct);
    Task MarkShippedAsync(long orderId, DateTime shippedUtc, DbTransaction tx, CancellationToken ct);

    Task<OrderForInvoice> GetForInvoiceAsync(long orderId, CancellationToken ct);
}
