using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

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

public sealed class Customer
{
    public CustomerId Id { get; }
    public string Name { get; }
    public bool IsBlocked { get; }
    public Money CreditLimit { get; }
    public Money OutstandingBalance { get; }

    public Customer(CustomerId id, string name, bool isBlocked, Money creditLimit, Money outstanding)
    { Id = id; Name = name; IsBlocked = isBlocked; CreditLimit = creditLimit; OutstandingBalance = outstanding; }

    public void EnsureNotBlocked()
    {
        if (IsBlocked)
            throw new DomainException($"Customer '{Name}' is blocked and cannot place orders.");
    }

    public void EnsureWithinCreditLimit(Money orderTotal)
    {
        if (OutstandingBalance + orderTotal > CreditLimit)
            throw new DomainException(
                $"Order total {orderTotal.Amount:C} would exceed the credit limit. " +
                $"Available credit: {(CreditLimit.Amount - OutstandingBalance.Amount):C}.");
    }
}

public sealed class ItemCategory
{
    public long Id { get; }
    public string Name { get; }
    public int MaxQuantityPerOrder { get; }
    public string? RequiredRole { get; }   // null = anyone

    public ItemCategory(long id, string name, int maxQuantityPerOrder, string? requiredRole)
    { Id = id; Name = name; MaxQuantityPerOrder = maxQuantityPerOrder; RequiredRole = requiredRole; }

    public void EnsureRoleAllowed(string callerRole)
    {
        if (RequiredRole is not null && !string.Equals(callerRole, RequiredRole, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(callerRole, "Admin", StringComparison.OrdinalIgnoreCase))
            throw new DomainException($"Category '{Name}' requires role '{RequiredRole}'.");
    }

    public void EnsureQuantityAllowed(int quantityOnOrder)
    {
        if (quantityOnOrder > MaxQuantityPerOrder)
            throw new DomainException(
                $"Category '{Name}' allows at most {MaxQuantityPerOrder} units per order (requested {quantityOnOrder}).");
    }
}

// Domain events (Phase 5 §8). IDomainEvent lives in BuildingBlocks; we just implement it.
public sealed record SalesOrderCreated(SalesOrderId Id, CustomerId CustomerId, UserId CreatedBy) : IDomainEvent;
public sealed record SalesOrderSubmitted(SalesOrderId Id, CustomerId CustomerId, Money Total) : IDomainEvent;
public sealed record SalesOrderApproved(SalesOrderId Id, CustomerId CustomerId, Money Total, UserId Approver) : IDomainEvent;
public sealed record SalesOrderRejected(SalesOrderId Id) : IDomainEvent;
public sealed record SalesOrderCancelled(SalesOrderId Id) : IDomainEvent;
