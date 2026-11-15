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
    Shipped = 5,
    PartiallyShipped = 6,   // some went out; the rest is still reserved and still to ship
    Closed = 7              // partly shipped, and the rest will never ship
}

internal static class SalesOrderStatusRules
{
    private static readonly Dictionary<SalesOrderStatus, SalesOrderStatus[]> Allowed = new()
    {
        [SalesOrderStatus.Draft]           = [SalesOrderStatus.PendingApproval, SalesOrderStatus.Cancelled],
        [SalesOrderStatus.PendingApproval] = [SalesOrderStatus.Approved, SalesOrderStatus.Rejected, SalesOrderStatus.Cancelled],
        [SalesOrderStatus.Approved]        = [SalesOrderStatus.Shipped, SalesOrderStatus.PartiallyShipped, SalesOrderStatus.Cancelled],
        [SalesOrderStatus.PartiallyShipped] = [SalesOrderStatus.PartiallyShipped, SalesOrderStatus.Shipped, SalesOrderStatus.Closed],
        [SalesOrderStatus.Shipped]         = [],
        [SalesOrderStatus.Closed]          = [],
        [SalesOrderStatus.Cancelled]       = [],
        [SalesOrderStatus.Rejected]        = [],
    };

    public static bool CanTransitionTo(this SalesOrderStatus from, SalesOrderStatus to) => Allowed[from].Contains(to);
}

// What a submitted order still waits for. Stored as a number: never renumber.
[Flags]
internal enum ApprovalNeeds
{
    None = 0,
    Manager = 1,         // above the customer's approval limit: a sales manager looks at it
    CreditRelease = 2    // over the credit limit: accounting decides whether to extend credit
}

internal static class ApprovalNeedsRules
{
    // Who may give which approval. Credit is finance's call, not the people paid to sell.
    public static ApprovalNeeds ClearableBy(string role) => role switch
    {
        Roles.Admin => ApprovalNeeds.Manager | ApprovalNeeds.CreditRelease,
        Roles.SalesManager => ApprovalNeeds.Manager,
        Roles.Accountant => ApprovalNeeds.CreditRelease,
        _ => ApprovalNeeds.None
    };

    // ["Manager", "CreditRelease"]: what the screens show as chips.
    public static IReadOnlyList<string> Names(this ApprovalNeeds needs) =>
        Enum.GetValues<ApprovalNeeds>().Where(n => n != ApprovalNeeds.None && needs.HasFlag(n)).Select(n => n.ToString()).ToList();
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
    public bool SpecialDiscountEligible { get; private set; }
    public decimal ShippedQuantity { get; private set; }
    public decimal Outstanding => Quantity.Value - ShippedQuantity;
    public Money LineTotal => UnitPrice * Quantity;
    public Money OutstandingTotal => new(Math.Round(UnitPrice.Amount * Outstanding, 2, MidpointRounding.AwayFromZero));

    private SalesOrderLine() { }

    public SalesOrderLine(ItemId itemId, string itemName, string unit, Quantity quantity, Money unitPrice, TaxType taxType,
        bool specialDiscountEligible)
    {
        ItemId = itemId; ItemName = itemName; Unit = unit; Quantity = quantity; UnitPrice = unitPrice; TaxType = taxType;
        SpecialDiscountEligible = specialDiscountEligible;
    }

    public void IncreaseQuantity(Quantity extra) => Quantity += extra;

    public void Ship(decimal quantity) => ShippedQuantity += quantity;
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
    public ApprovalNeeds PendingApprovals { get; private set; }
    public UserId? ApprovedBy { get; private set; }   // the manager who approved its size; null = within the approval limit
    public DateTime? ApprovedUtc { get; private set; }
    public UserId? CreditReleasedBy { get; private set; }   // null = within the credit limit
    public DateTime? CreditReleasedUtc { get; private set; }
    public UserId? RejectedBy { get; private set; }
    public DateTime? RejectedUtc { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTime? ShippedUtc { get; private set; }
    public SpecialDiscount? SpecialDiscount { get; private set; }
    // Stored, so the read side can show the total without repeating the calculation in SQL.
    public Money SpecialDiscountVatExemption { get; private set; }
    public Money SpecialDiscountAmount { get; private set; }

    public Money GrossTotal => _lines.Aggregate(Money.Zero, (sum, l) => sum + l.LineTotal);
    public Money Total => GrossTotal - SpecialDiscountVatExemption - SpecialDiscountAmount;
    public bool HoldsStock => Status is SalesOrderStatus.PendingApproval or SalesOrderStatus.Approved or SalesOrderStatus.PartiallyShipped;

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
        if (lines.Count == 0) throw new DomainException(SalesErrors.OrderNeedsLines, "An order needs at least one line.");

        // Built aside and swapped in at the end, so a refused line leaves the order exactly as it was.
        var newLines = new List<SalesOrderLine>();
        var perCategory = new Dictionary<CategoryId, decimal>();
        foreach (var (item, quantity) in lines)
        {
            var category = item.EnsureSellable();
            category.EnsureRoleAllowed(callerRole);
            item.EnsureQuantityFits(quantity);
            perCategory[category.Id] = perCategory.GetValueOrDefault(category.Id) + quantity.Value;
            category.EnsureQuantityAllowed(perCategory[category.Id]);

            var existing = newLines.FirstOrDefault(l => l.ItemId == item.Id);
            if (existing is not null) existing.IncreaseQuantity(quantity);
            else newLines.Add(new SalesOrderLine(item.Id, item.Name, item.Unit, quantity, item.UnitPrice, item.TaxType,
                item.IsSpecialDiscountEligible));
        }
        _lines.Clear();
        _lines.AddRange(newLines);
        RecalculateSpecialDiscount();
    }

    // One per order: a buyer who is both a senior citizen and a PWD still gets one discount.
    public void ApplySpecialDiscount(SpecialDiscount discount)
    {
        EnsureDiscountChangeable();
        if (!_lines.Any(l => l.SpecialDiscountEligible))
            throw new DomainException(SalesErrors.NoLineQualifiesForDiscount, $"No item on {OrderNumber} qualifies for the {discount.Program} discount.");
        SpecialDiscount = discount;
        RecalculateSpecialDiscount();
    }

    public void RemoveSpecialDiscount()
    {
        EnsureDiscountChangeable();
        SpecialDiscount = null;
        RecalculateSpecialDiscount();
    }

    // otherPendingTotal: the customer's other submitted-but-unapproved orders, which already count against credit.
    // Nothing is refused for size or credit here: the order waits for the people who may say yes.
    public void Submit(Customer customer, SalesSettings settings, Money otherPendingTotal, DateTime nowUtc)
    {
        EnsureBelongsTo(customer);
        if (_lines.Count == 0)
            throw new DomainException(SalesErrors.OrderNeedsLines, "Cannot submit an order with no lines.");
        customer.EnsureNotBlocked();

        Transition(SalesOrderStatus.PendingApproval);
        SubmittedUtc = nowUtc;
        PendingApprovals = ApprovalNeeds.None;
        if (Total > customer.ApprovalLimit(settings)) PendingApprovals |= ApprovalNeeds.Manager;
        if (!customer.IsWithinCreditLimit(Total, otherPendingTotal)) PendingApprovals |= ApprovalNeeds.CreditRelease;

        if (PendingApprovals == ApprovalNeeds.None)
            MarkApproved(customer, nowUtc);
    }

    // Gives every approval the approver's role may give. The order is approved once nothing is left to wait for.
    public void Approve(UserId approver, string role, Customer customer, DateTime nowUtc)
    {
        EnsureBelongsTo(customer);
        EnsurePending();
        if (approver == CreatedBy)
            throw new DomainException(SalesErrors.CreatorCannotApprove, "An order can't be approved by the person who created it.");

        var giving = PendingApprovals & ApprovalNeedsRules.ClearableBy(role);
        if (giving == ApprovalNeeds.None)
            throw PendingApprovals.HasFlag(ApprovalNeeds.CreditRelease)
                ? new DomainException(SalesErrors.CreditLimitExceeded, $"{OrderNumber} is over the customer's credit limit; accounting must release it.")
                : new DomainException(SalesErrors.NotYourApproval, $"{OrderNumber} waits for a sales manager.");

        if (giving.HasFlag(ApprovalNeeds.Manager)) ApprovedBy = approver;
        if (giving.HasFlag(ApprovalNeeds.CreditRelease)) (CreditReleasedBy, CreditReleasedUtc) = (approver, nowUtc);
        PendingApprovals &= ~giving;

        if (PendingApprovals == ApprovalNeeds.None)
            MarkApproved(customer, nowUtc);
    }

    // Anyone whose approval the order waits for may refuse it instead. Returns the lines whose reserved stock must be released.
    public IReadOnlyList<SalesOrderLine> Reject(UserId rejectedBy, string role, string reason, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException(SalesErrors.RejectionReasonRequired, "A rejection needs a reason.");
        EnsurePending();
        if ((PendingApprovals & ApprovalNeedsRules.ClearableBy(role)) == ApprovalNeeds.None)
            throw new DomainException(SalesErrors.NotYourApproval, $"{OrderNumber} doesn't wait for your approval.");
        Transition(SalesOrderStatus.Rejected);
        PendingApprovals = ApprovalNeeds.None;
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
        PendingApprovals = ApprovalNeeds.None;

        if (wasCharged)
            customer.DeductFromBalance(Total);
        return heldStock ? _lines : [];
    }

    public void EnsureCanShip()
    {
        if (Status is not (SalesOrderStatus.Approved or SalesOrderStatus.PartiallyShipped))
            throw new DomainException(SalesErrors.OnlyApprovedOrdersShip, $"Only approved orders can be shipped; {OrderNumber} is {Status}.");
    }

    // What left in one shipment. Each line may ship in parts, never more than is still to ship.
    public void RecordShipment(IReadOnlyList<(ItemId ItemId, decimal Quantity)> shipped, DateTime nowUtc)
    {
        EnsureCanShip();
        if (shipped.Count == 0 || shipped.Any(s => s.Quantity <= 0))
            throw new DomainException(SalesErrors.ShipmentExceedsOrder, $"A shipment of {OrderNumber} needs a quantity on at least one line.");
        // Checked first, applied after: a refused line leaves the order exactly as it was.
        var lines = shipped.GroupBy(s => s.ItemId).Select(g =>
        {
            var line = _lines.SingleOrDefault(l => l.ItemId == g.Key)
                ?? throw new DomainException(SalesErrors.ShipmentExceedsOrder, $"Item {g.Key.Value} is not on {OrderNumber}.");
            var quantity = g.Sum(s => s.Quantity);
            if (quantity > line.Outstanding)
                throw new DomainException(SalesErrors.ShipmentExceedsOrder,
                    $"{line.ItemName}: {quantity:0.###} {line.Unit} would ship, but only {line.Outstanding:0.###} is still to ship.");
            return (line, quantity);
        }).ToList();

        var complete = _lines.All(l => lines.Any(s => s.line == l && s.quantity == l.Outstanding) || l.Outstanding == 0);
        // The discount was computed on the whole order; split across invoices, the centavos wouldn't add up.
        if (SpecialDiscount is not null && !complete)
            throw new DomainException(SalesErrors.DiscountedOrderShipsWhole, $"{OrderNumber} has a {SpecialDiscount.Program} discount and ships in one go.");

        foreach (var (line, quantity) in lines) line.Ship(quantity);
        Transition(complete ? SalesOrderStatus.Shipped : SalesOrderStatus.PartiallyShipped);
        ShippedUtc = nowUtc;
    }

    // The rest won't ship: what's still reserved is released, and the customer is no longer charged for it.
    // Returns the lines with stock to release and how much.
    public IReadOnlyList<(ItemId ItemId, decimal Quantity)> CloseShort(Customer customer)
    {
        EnsureBelongsTo(customer);
        Transition(SalesOrderStatus.Closed);
        var unshipped = _lines.Where(l => l.Outstanding > 0).ToList();
        customer.DeductFromBalance(unshipped.Aggregate(Money.Zero, (sum, l) => sum + l.OutstandingTotal));
        return unshipped.Select(l => (l.ItemId, l.Outstanding)).ToList();
    }

    private void MarkApproved(Customer customer, DateTime nowUtc)
    {
        Transition(SalesOrderStatus.Approved);
        ApprovedUtc = nowUtc;
        customer.AddToBalance(Total);
        Raise(new SalesOrderApproved(Id, CustomerId, Total, ApprovedBy));
    }

    private void EnsurePending()
    {
        if (Status != SalesOrderStatus.PendingApproval)
            throw new DomainException(SalesErrors.InvalidStatusChange, $"{OrderNumber} is {Status}, not waiting for approval.");
    }

    private void RecalculateSpecialDiscount()
    {
        var amounts = SpecialDiscount?.Apply(_lines.Select(l => (l.LineTotal, l.TaxType, l.SpecialDiscountEligible)))
            ?? SpecialDiscountAmounts.None;
        (SpecialDiscountVatExemption, SpecialDiscountAmount) = (amounts.VatExemption, amounts.Discount);
    }

    private void EnsureDiscountChangeable()
    {
        if (Status != SalesOrderStatus.Draft)
            throw new DomainException(SalesErrors.DiscountOnlyOnDraft, "A special discount can only be changed on a draft order.");
    }

    private void EnsureDraft()
    {
        if (Status != SalesOrderStatus.Draft)
            throw new DomainException(SalesErrors.LinesOnlyOnDraft, "Lines can only be changed on a draft order.");
    }

    private void EnsureBelongsTo(Customer customer)
    {
        if (customer.Id != CustomerId)
            throw new InvalidOperationException($"Customer {customer.Id.Value} does not own order {OrderNumber}.");
    }

    private void Transition(SalesOrderStatus to)
    {
        if (!Status.CanTransitionTo(to))
            throw new DomainException(SalesErrors.InvalidStatusChange, $"Cannot move a {Status} order to {to}.");
        Status = to;
    }
}

// Raise an event only in the change that adds its first handler.
internal sealed record SalesOrderApproved(SalesOrderId Id, CustomerId CustomerId, Money Total, UserId? ApprovedBy) : IDomainEvent;
