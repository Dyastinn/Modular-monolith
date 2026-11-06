namespace Erp.Web.Models;

// The API's JSON shapes, as the browser sees them. The server's own types are internal to each module, so the
// browser app keeps its own copies: the HTTP contract is the only thing the two share.

public sealed record SalesOrderSummary(long Id, string OrderNumber, long CustomerId, string CustomerName, string Status,
    decimal Total, long CreatedBy, DateTime CreatedUtc);

public sealed record SalesOrderLine(long ItemId, string ItemName, string Unit, decimal Quantity, decimal UnitPrice, decimal LineTotal,
    bool SpecialDiscountEligible);

public sealed record SpecialDiscount(string Program, string IdLabel, string HolderName, string IdNumber, decimal Rate,
    decimal VatRateRemoved, decimal VatExemption, decimal Discount);

public sealed record SpecialDiscountProgram(string Code, string Name, string IdLabel, decimal Rate, bool VatExempt, bool IsActive);

public sealed record CompanyProfileModel(string RegisteredName, string? TradeName, string Tin, string RegisteredAddress, string TaxRegime,
    decimal VatRate, string InvoiceTitle, string? PermitNumber, string? SeriesRange, string? PermitDateIssued, long? UpdatedBy,
    DateTime? UpdatedUtc, bool IsComplete);

public sealed record SalesOrderDetail(long Id, string OrderNumber, long CustomerId, string CustomerName, string Status, decimal Total,
    long CreatedBy, DateTime CreatedUtc, DateTime? SubmittedUtc, long? ApprovedBy, DateTime? ApprovedUtc, long? RejectedBy,
    string? RejectionReason, DateTime? ShippedUtc, decimal GrossTotal, SpecialDiscount? SpecialDiscount, List<SalesOrderLine> Lines);

public sealed record Customer(long Id, string Name, string? Tin, string? Address, int PaymentTermsDays, bool IsBlocked,
    decimal CreditLimit, decimal OutstandingBalance, decimal PendingTotal, decimal AvailableCredit);

public sealed record PriceListItem(long Id, string Sku, string Name, string Unit, bool AllowsFractions, bool IsActive,
    decimal UnitPrice, long? CategoryId, string? CategoryName, bool IsForSale, string TaxType, bool IsSpecialDiscountEligible);

public sealed record ItemCategory(long Id, string Name, decimal MaxQuantityPerOrder, string? RequiredRole);

public sealed record StockItem(long Id, string Sku, string Name, string Unit, bool AllowsFractions, bool IsActive,
    decimal OnHand, decimal Reserved, decimal Available);

public sealed record StockMovement(long Id, decimal Quantity, string Kind, string? Reference, string? Reason, long UserId, DateTime OccurredUtc);

public sealed record Shipment(long Id, string Number, long SalesOrderId, string OrderNumber, long ShippedBy, DateTime ShippedUtc, int LineCount);

public sealed record Supplier(long Id, string Name, string? Tin, string? Address, string? Email, string? Phone, int PaymentTermsDays, bool IsActive);

public sealed record PurchaseOrderSummary(long Id, string Number, long SupplierId, string SupplierName, string Status, decimal Total,
    DateOnly? ExpectedDate, DateTime CreatedUtc);

public sealed record PurchaseOrderLine(long Id, long ItemId, string Sku, string ItemName, string Unit, decimal Quantity,
    decimal ReceivedQuantity, decimal Outstanding, decimal UnitCost, decimal LineTotal);

public sealed record PurchaseOrderDetail(long Id, string Number, long SupplierId, string SupplierName, string Status, decimal Total,
    DateOnly? ExpectedDate, string? Notes, long CreatedBy, DateTime CreatedUtc, long? ApprovedBy, DateTime? ApprovedUtc,
    DateTime? SentUtc, List<PurchaseOrderLine> Lines);

public sealed record InvoiceSummary(long Id, string Number, long CustomerId, string CustomerName, string OrderNumber,
    DateOnly IssueDate, DateOnly DueDate, string Status, decimal Total, decimal AmountPaid, decimal BalanceDue);

public sealed record InvoiceLine(string Description, string Unit, decimal Quantity, decimal UnitPrice, decimal LineTotal, string TaxType,
    bool SpecialDiscountEligible);

public sealed record Payment(long Id, decimal Amount, decimal TaxWithheld, DateOnly PaidOn, string Method, string? Reference, long RecordedBy,
    DateTime RecordedUtc, DateTime? VoidedUtc, string? VoidReason);

public sealed record InvoiceDetail(long Id, string Number, long ShipmentId, long SalesOrderId, string OrderNumber, long CustomerId,
    string CustomerName, string? CustomerTin, string? CustomerAddress, DateOnly IssueDate, DateOnly DueDate, string Status,
    decimal Total, decimal AmountPaid, decimal BalanceDue, string TaxRegime, decimal VatRate, decimal VatableSales, decimal VatAmount,
    decimal ZeroRatedSales, decimal VatExemptSales, int PrintCount, string? VoidReason, long? VoidedBy,
    DateTime? VoidedUtc, SpecialDiscount? SpecialDiscount, List<InvoiceLine> Lines, List<Payment> Payments, decimal GrossSales);

public sealed record WithholdingRow(long CustomerId, string CustomerName, int Quarter, decimal Withheld, decimal Certified, decimal Missing);

public sealed record WithholdingCertificate(long Id, long CustomerId, string CustomerName, int Quarter, decimal Amount, string? Reference,
    DateOnly ReceivedOn, long RecordedBy, DateTime RecordedUtc, DateTime? VoidedUtc, string? VoidReason);

public sealed record WithholdingReport(int Year, List<WithholdingRow> Summary, List<WithholdingCertificate> Certificates);

public sealed record AgingRow(long CustomerId, string CustomerName, decimal NotYetDue, decimal Days1To30, decimal Days31To60,
    decimal Days61To90, decimal Over90, decimal Total);

public sealed record AgingReport(DateOnly AsOf, List<AgingRow> Customers, AgingRow Totals);

public sealed record User(long Id, string Email, string FullName, string Role, bool IsDisabled, bool MustChangePassword,
    DateTime? LockedOutUntil, DateTime? LastSignIn);

public sealed record SignInAttempt(long Id, string Email, long? UserId, string Outcome, string? IpAddress, DateTime OccurredUtc);
