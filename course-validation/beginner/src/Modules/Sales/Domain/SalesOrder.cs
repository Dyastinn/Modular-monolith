using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

public sealed class SalesOrder : AggregateRoot<SalesOrderId>
{
    private readonly List<SalesOrderLine> _lines = new();
    public IReadOnlyList<SalesOrderLine> Lines => _lines;
    public CustomerId CustomerId { get; private set; }
    public SalesOrderStatus Status { get; private set; }

    // NOTE (validation): Phase 5 §5 says "the repository loads the rowversion with the order and passes
    // it back on update," but the domain has nowhere to keep it. Holding it here leaks an infrastructure
    // concurrency token into the pure domain — the plan never resolves this tension.
    public byte[]? RowVersion { get; private set; }

    // Total is COMPUTED from the lines — it literally cannot be wrong.
    public Money Total => _lines.Aggregate(Money.Zero, (sum, l) => sum + l.LineTotal);

    private SalesOrder() { }  // private — only the repository uses this to rebuild from the DB (Phase 4)

    // Rehydrate from DB rows. The plan says "rebuild via the private constructor" but never shows how;
    // with private setters + events raised in AddLine, a dedicated factory is the practical mechanism.
    public static SalesOrder Rehydrate(SalesOrderId id, CustomerId customerId, SalesOrderStatus status,
        IEnumerable<SalesOrderLine> lines, byte[]? rowVersion = null)
    {
        var order = new SalesOrder { CustomerId = customerId, Status = status };
        order.Id = id;
        order._lines.AddRange(lines);
        order.RowVersion = rowVersion;
        return order;
    }

    // The only way to make a new SalesOrder — and it starts as Draft.
    public static SalesOrder Create(CustomerId customerId)
    {
        var order = new SalesOrder { CustomerId = customerId, Status = SalesOrderStatus.Draft };
        order.Raise(new SalesOrderCreated(customerId));   // no id yet — assigned by the DB on insert
        return order;
    }

    public void AddLine(ProductId product, Quantity qty, Money unitPrice)
    {
        if (Status != SalesOrderStatus.Draft)
            throw new DomainException("Lines can only be added to a draft order.");
        _lines.Add(new SalesOrderLine(product, qty, unitPrice));
    }

    public void Submit(bool approvalGranted)
    {
        if (Status != SalesOrderStatus.Draft)
            throw new DomainException($"Only draft orders can be submitted (was {Status}).");
        if (_lines.Count == 0)
            throw new DomainException("Cannot submit an empty order.");
        if (RequiresApproval && !approvalGranted)
            throw new DomainException("Order exceeds approval threshold and needs manager approval.");
        Status = SalesOrderStatus.Submitted;
        Raise(new SalesOrderSubmitted(Id, CustomerId, Total));
    }

    public void Cancel()
    {
        if (!Status.CanTransitionTo(SalesOrderStatus.Cancelled))
            throw new DomainException($"Cannot cancel an order in status {Status}.");
        Status = SalesOrderStatus.Cancelled;
        Raise(new SalesOrderCancelled(Id));
    }

    // The approval threshold is a BUSINESS RULE, so it lives here — not in a handler.
    public bool RequiresApproval => Total > ApprovalPolicy.Threshold;
}
