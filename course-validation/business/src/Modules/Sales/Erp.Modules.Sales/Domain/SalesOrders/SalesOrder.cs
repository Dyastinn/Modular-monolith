using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

internal sealed class SalesOrder : AggregateRoot<SalesOrderId>
{
    private readonly List<SalesOrderLine> _lines = new();
    public IReadOnlyList<SalesOrderLine> Lines => _lines;

    public string OrderNumber { get; private set; } = default!;
    public CustomerId CustomerId { get; private set; }
    public SalesOrderStatus Status { get; private set; }
    public UserId CreatedBy { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public UserId? ApprovedBy { get; private set; }
    public DateTime? ApprovedUtc { get; private set; }

    // Total is COMPUTED — it can never disagree with the lines.
    public Money Total => _lines.Aggregate(Money.Zero, (sum, l) => sum + l.LineTotal);

    // The approval threshold is the ORDER'S own knowledge (a surfaced high-value flag).
    public bool RequiresApproval => Total > ApprovalPolicy.Threshold;

    private SalesOrder() { }   // EF rebuilds saved orders through this; it runs no rules and raises no events

    public static SalesOrder Create(string orderNumber, CustomerId customerId, UserId createdBy, DateTime nowUtc)
    {
        var order = new SalesOrder
        {
            OrderNumber = orderNumber,
            CustomerId = customerId,
            CreatedBy = createdBy,
            CreatedUtc = nowUtc,
            Status = SalesOrderStatus.Draft
        };
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
    }

    public void Approve(UserId approver, DateTime nowUtc)
    {
        Transition(SalesOrderStatus.Approved);
        ApprovedBy = approver;
        ApprovedUtc = nowUtc;
        Raise(new SalesOrderApproved(Id, CustomerId, Total, approver));
    }

    public void Reject()
    {
        Transition(SalesOrderStatus.Rejected);
    }

    public void Cancel()
    {
        Transition(SalesOrderStatus.Cancelled);
    }

    // Single guarded gate for EVERY status change.
    private void Transition(SalesOrderStatus to)
    {
        if (!Status.CanTransitionTo(to))
            throw new DomainException($"Cannot move a {Status} order to {to}.");
        Status = to;
    }
}
