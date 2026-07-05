using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

public readonly record struct SalesOrderId(long Value);
public readonly record struct CustomerId(long Value);
public readonly record struct ItemId(long Value);
public readonly record struct UserId(long Value);

public readonly record struct Quantity
{
    public int Value { get; }
    public Quantity(int value) =>
        Value = value > 0 ? value : throw new DomainException("Quantity must be greater than zero.");
}

public readonly record struct Money
{
    public decimal Amount { get; }
    public static readonly Money Zero = new(0m);
    public Money(decimal amount) =>
        Amount = amount >= 0 ? amount : throw new DomainException("Money cannot be negative.");

    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount);
    public static Money operator *(Money m, int qty) => new(m.Amount * qty);
    public static bool operator >(Money a, Money b) => a.Amount > b.Amount;
    public static bool operator <(Money a, Money b) => a.Amount < b.Amount;
    public static bool operator >=(Money a, Money b) => a.Amount >= b.Amount;
    public static bool operator <=(Money a, Money b) => a.Amount <= b.Amount;
}

public enum SalesOrderStatus
{
    Draft = 0,            // being edited
    PendingApproval = 1,  // submitted, inventory reserved, awaiting a manager
    Approved = 2,         // manager approved; will be fulfilled
    Cancelled = 3,        // cancelled; reservations released
    Rejected = 4          // manager rejected; reservations released
}

public static class SalesOrderStatusRules
{
    private static readonly Dictionary<SalesOrderStatus, SalesOrderStatus[]> _allowed = new()
    {
        [SalesOrderStatus.Draft]           = new[] { SalesOrderStatus.PendingApproval, SalesOrderStatus.Cancelled },
        [SalesOrderStatus.PendingApproval] = new[] { SalesOrderStatus.Approved, SalesOrderStatus.Rejected, SalesOrderStatus.Cancelled },
        [SalesOrderStatus.Approved]        = new[] { SalesOrderStatus.Cancelled },
        [SalesOrderStatus.Cancelled]       = Array.Empty<SalesOrderStatus>(),
        [SalesOrderStatus.Rejected]        = Array.Empty<SalesOrderStatus>(),
    };

    public static bool CanTransitionTo(this SalesOrderStatus from, SalesOrderStatus to) =>
        _allowed[from].Contains(to);
}

public static class ApprovalPolicy
{
    public static readonly Money Threshold = new(10_000m);  // orders over $10k are flagged high-value
}
