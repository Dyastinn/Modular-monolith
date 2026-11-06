using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

// Stored as numbers: never renumber.
internal enum SalesOrderStatus
{
    Draft = 0,
    PendingApproval = 1,
    Approved = 2,
    Cancelled = 3,
    Rejected = 4,
    Shipped = 5
}

internal static class SalesOrderStatusRules
{
    private static readonly Dictionary<SalesOrderStatus, SalesOrderStatus[]> Allowed = new()
    {
        [SalesOrderStatus.Draft]           = [SalesOrderStatus.PendingApproval, SalesOrderStatus.Cancelled],
        [SalesOrderStatus.PendingApproval] = [SalesOrderStatus.Approved, SalesOrderStatus.Rejected, SalesOrderStatus.Cancelled],
        [SalesOrderStatus.Approved]        = [SalesOrderStatus.Shipped, SalesOrderStatus.Cancelled],
        [SalesOrderStatus.Shipped]         = [],
        [SalesOrderStatus.Cancelled]       = [],
        [SalesOrderStatus.Rejected]        = [],
    };

    public static bool CanTransitionTo(this SalesOrderStatus from, SalesOrderStatus to) => Allowed[from].Contains(to);
}

internal static class ApprovalPolicy
{
    // Orders above this need a manager; at or below it they're approved on submit.
    public static readonly Money Threshold = new(50_000m);
}

// The line keeps the item's name and unit as they were when ordered, like a printed order would.
internal sealed class SalesOrderLine
{
    public ItemId ItemId { get; private set; }
    public string ItemName { get; private set; } = default!;
    public string Unit { get; private set; } = default!;
    public Quantity Quantity { get; private set; }
    public Money UnitPrice { get; private set; }
    public TaxType TaxType { get; private set; }
    public Money LineTotal => UnitPrice * Quantity;

    private SalesOrderLine() { }

    public SalesOrderLine(ItemId itemId, string itemName, string unit, Quantity quantity, Money unitPrice, TaxType taxType)
    { ItemId = itemId; ItemName = itemName; Unit = unit; Quantity = quantity; UnitPrice = unitPrice; TaxType = taxType; }

    public void IncreaseQuantity(Quantity extra) => Quantity += extra;
}

internal sealed class SalesOrder : AggregateRoot<SalesOrderId>
{
    private readonly List<SalesOrderLine> _lines = new();
    public IReadOnlyList<SalesOrderLine> Lines => _lines;

    public string OrderNumber { get; private set; } = default!;
    public CustomerId CustomerId { get; private set; }
    public SalesOrderStatus Status { get; private set; }
    public UserId CreatedBy { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime? SubmittedUtc { get; private set; }
    public UserId? ApprovedBy { get; private set; }   // null = approved automatically
    public DateTime? ApprovedUtc { get; private set; }
    public UserId? RejectedBy { get; private set; }
    public DateTime? RejectedUtc { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTime? ShippedUtc { get; private set; }

    public Money Total => _lines.Aggregate(Money.Zero, (sum, l) => sum + l.LineTotal);
    public bool RequiresApproval => Total > ApprovalPolicy.Threshold;
    public bool HoldsStock => Status is SalesOrderStatus.PendingApproval or SalesOrderStatus.Approved;

    private SalesOrder() { }

    public static SalesOrder Create(string orderNumber, CustomerId customerId, UserId createdBy, DateTime nowUtc) => new()
    {
        OrderNumber = orderNumber,
        CustomerId = customerId,
        CreatedBy = createdBy,
        CreatedUtc = nowUtc,
        Status = SalesOrderStatus.Draft
    };

    // Reps work their own orders; managers and admins see everyone's. Anyone else on staff only reads.
    public static UserId? CreatorFilterFor(UserId user, string role) => role == Roles.SalesRep ? user : null;

    public bool IsVisibleTo(UserId user, string role) => CreatorFilterFor(user, role) is not { } only || only == CreatedBy;

    // Replaces every line. The category caps count across the whole order, so they're checked on the full set.
    public void SetLines(IReadOnlyList<(SalesItem Item, Quantity Quantity)> lines, string callerRole)
    {
        EnsureDraft();
        if (lines.Count == 0) throw new DomainException("An order needs at least one line.");
        _lines.Clear();

        var perCategory = new Dictionary<CategoryId, decimal>();
        foreach (var (item, quantity) in lines)
        {
            var category = item.EnsureSellable();
            category.EnsureRoleAllowed(callerRole);
            item.EnsureQuantityFits(quantity);
            perCategory[category.Id] = perCategory.GetValueOrDefault(category.Id) + quantity.Value;
            category.EnsureQuantityAllowed(perCategory[category.Id]);

            var existing = _lines.FirstOrDefault(l => l.ItemId == item.Id);
            if (existing is not null) existing.IncreaseQuantity(quantity);
            else _lines.Add(new SalesOrderLine(item.Id, item.Name, item.Unit, quantity, item.UnitPrice, item.TaxType));
        }
    }

    // otherPendingTotal: the customer's other submitted-but-unapproved orders, which already count against credit.
    public void Submit(Customer customer, Money otherPendingTotal, DateTime nowUtc)
    {
        EnsureBelongsTo(customer);
        if (_lines.Count == 0)
            throw new DomainException("Cannot submit an order with no lines.");
        customer.EnsureNotBlocked();
        customer.EnsureWithinCreditLimit(Total, otherPendingTotal);

        Transition(SalesOrderStatus.PendingApproval);
        SubmittedUtc = nowUtc;

        if (!RequiresApproval)
            MarkApproved(approver: null, customer, nowUtc);
    }

    public void Approve(UserId approver, Customer customer, DateTime nowUtc)
    {
        EnsureBelongsTo(customer);
        if (approver == CreatedBy)
            throw new DomainException("An order can't be approved by the person who created it.");
        MarkApproved(approver, customer, nowUtc);
    }

    // Returns the lines whose reserved stock must be released.
    public IReadOnlyList<SalesOrderLine> Reject(UserId rejectedBy, string reason, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A rejection needs a reason.");
        Transition(SalesOrderStatus.Rejected);
        RejectedBy = rejectedBy;
        RejectedUtc = nowUtc;
        RejectionReason = reason.Trim();
        return _lines;
    }

    // Returns the lines whose reserved stock must be released (none for a draft).
    public IReadOnlyList<SalesOrderLine> Cancel(Customer customer)
    {
        EnsureBelongsTo(customer);
        var heldStock = HoldsStock;
        var wasCharged = Status == SalesOrderStatus.Approved;

        Transition(SalesOrderStatus.Cancelled);

        if (wasCharged)
            customer.DeductFromBalance(Total);
        return heldStock ? _lines : [];
    }

    public void EnsureCanShip()
    {
        if (Status != SalesOrderStatus.Approved)
            throw new DomainException($"Only approved orders can be shipped; {OrderNumber} is {Status}.");
    }

    public void MarkShipped(DateTime nowUtc)
    {
        EnsureCanShip();
        Transition(SalesOrderStatus.Shipped);
        ShippedUtc = nowUtc;
    }

    private void MarkApproved(UserId? approver, Customer customer, DateTime nowUtc)
    {
        Transition(SalesOrderStatus.Approved);
        ApprovedBy = approver;
        ApprovedUtc = nowUtc;
        customer.AddToBalance(Total);
        Raise(new SalesOrderApproved(Id, CustomerId, Total, approver));
    }

    private void EnsureDraft()
    {
        if (Status != SalesOrderStatus.Draft)
            throw new DomainException("Lines can only be changed on a draft order.");
    }

    private void EnsureBelongsTo(Customer customer)
    {
        if (customer.Id != CustomerId)
            throw new InvalidOperationException($"Customer {customer.Id.Value} does not own order {OrderNumber}.");
    }

    private void Transition(SalesOrderStatus to)
    {
        if (!Status.CanTransitionTo(to))
            throw new DomainException($"Cannot move a {Status} order to {to}.");
        Status = to;
    }
}

// Raise an event only in the change that adds its first handler.
internal sealed record SalesOrderApproved(SalesOrderId Id, CustomerId CustomerId, Money Total, UserId? ApprovedBy) : IDomainEvent;
