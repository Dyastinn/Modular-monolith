using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

// Everything that IS the SalesOrder aggregate lives here: the root, its child line, the lifecycle it
// moves through, the events it raises, and the one policy constant it consults. They share a file
// because they share a reason to change — the sales order's own rules (Phase 5 §4, §6, §8).

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
    public static readonly Money Threshold = new(50_000m);  // orders over ₱50,000 are flagged high-value
}

public sealed class SalesOrder : AggregateRoot<SalesOrderId>
{
    private readonly List<SalesOrderLine> _lines = new();
    public IReadOnlyList<SalesOrderLine> Lines => _lines;

    public string OrderNumber { get; private set; } = default!;
    public CustomerId CustomerId { get; private set; }
    public SalesOrderStatus Status { get; private set; }
    public UserId CreatedBy { get; private set; }
    public UserId? ApprovedBy { get; private set; }
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    // Total is COMPUTED — it can never disagree with the lines.
    public Money Total => _lines.Aggregate(Money.Zero, (sum, l) => sum + l.LineTotal);

    // The approval threshold is the ORDER'S own knowledge (a surfaced high-value flag; see Phase 5 §8).
    public bool RequiresApproval => Total > ApprovalPolicy.Threshold;

    private SalesOrder() { }   // for the repository to rehydrate from the DB

    public static SalesOrder Create(string orderNumber, CustomerId customerId, UserId createdBy)
    {
        var order = new SalesOrder
        {
            OrderNumber = orderNumber,
            CustomerId = customerId,
            CreatedBy = createdBy,
            Status = SalesOrderStatus.Draft
        };
        order.Raise(new SalesOrderCreated(order.Id, customerId, createdBy));
        return order;
    }

    // Rebuild a persisted order from the database (read side of the repository). Sets state directly
    // WITHOUT raising events — this is loading history, not a new business fact.
    // (The tutorial leaves GetAsync's body "omitted, same shape"; this is the rehydration seam it implies.)
    public static SalesOrder Rehydrate(
        SalesOrderId id, string orderNumber, CustomerId customerId, SalesOrderStatus status,
        UserId createdBy, UserId? approvedBy, byte[] rowVersion, IEnumerable<SalesOrderLine> lines)
    {
        var order = new SalesOrder
        {
            Id = id,
            OrderNumber = orderNumber,
            CustomerId = customerId,
            Status = status,
            CreatedBy = createdBy,
            ApprovedBy = approvedBy,
            RowVersion = rowVersion
        };
        order._lines.AddRange(lines);
        return order;
    }

    public void AddLine(ItemId itemId, Quantity qty, Money unitPrice)
    {
        if (Status != SalesOrderStatus.Draft)
            throw new DomainException("Lines can only be changed on a draft order.");
        var existing = _lines.FirstOrDefault(l => l.ItemId == itemId);
        if (existing is not null) existing.IncreaseQuantity(qty);
        else _lines.Add(new SalesOrderLine(itemId, qty, unitPrice));
    }

    public void ClearLines()
    {
        if (Status != SalesOrderStatus.Draft)
            throw new DomainException("Lines can only be changed on a draft order.");
        _lines.Clear();
    }

    public void Submit()
    {
        if (_lines.Count == 0)
            throw new DomainException("Cannot submit an order with no lines.");
        Transition(SalesOrderStatus.PendingApproval);
        Raise(new SalesOrderSubmitted(Id, CustomerId, Total));
    }

    public void Approve(UserId approver)
    {
        Transition(SalesOrderStatus.Approved);
        ApprovedBy = approver;
        Raise(new SalesOrderApproved(Id, CustomerId, Total, approver));
    }

    public void Reject()
    {
        Transition(SalesOrderStatus.Rejected);
        Raise(new SalesOrderRejected(Id));
    }

    public void Cancel()
    {
        Transition(SalesOrderStatus.Cancelled);
        Raise(new SalesOrderCancelled(Id));
    }

    // Single guarded gate for EVERY status change.
    private void Transition(SalesOrderStatus to)
    {
        if (!Status.CanTransitionTo(to))
            throw new DomainException($"Cannot move a {Status} order to {to}.");
        Status = to;
    }
}

public sealed class SalesOrderLine
{
    public ItemId ItemId { get; }
    public Quantity Quantity { get; private set; }
    public Money UnitPrice { get; }
    public Money LineTotal => UnitPrice * Quantity.Value;

    public SalesOrderLine(ItemId itemId, Quantity qty, Money unitPrice)
    { ItemId = itemId; Quantity = qty; UnitPrice = unitPrice; }

    public void IncreaseQuantity(Quantity extra) => Quantity = new Quantity(Quantity.Value + extra.Value);
}

// Domain events (Phase 5 §8). IDomainEvent lives in BuildingBlocks; we just implement it.
public sealed record SalesOrderCreated(SalesOrderId Id, CustomerId CustomerId, UserId CreatedBy) : IDomainEvent;
public sealed record SalesOrderSubmitted(SalesOrderId Id, CustomerId CustomerId, Money Total) : IDomainEvent;
public sealed record SalesOrderApproved(SalesOrderId Id, CustomerId CustomerId, Money Total, UserId Approver) : IDomainEvent;
public sealed record SalesOrderRejected(SalesOrderId Id) : IDomainEvent;
public sealed record SalesOrderCancelled(SalesOrderId Id) : IDomainEvent;
