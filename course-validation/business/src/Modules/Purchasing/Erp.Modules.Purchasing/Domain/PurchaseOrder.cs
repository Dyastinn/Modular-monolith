using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Purchasing.Domain;

// Stored as numbers: never renumber.
internal enum PurchaseOrderStatus
{
    Draft = 0,
    Approved = 1,
    Sent = 2,
    PartiallyReceived = 3,
    Received = 4,
    Closed = 5,      // partly received, and the rest will never come
    Cancelled = 6
}

// What we buy, from whom, at what cost. Snapshot of the item as it was when ordered.
internal sealed class PurchaseOrderLine
{
    public long Id { get; private set; }
    public ItemId ItemId { get; private set; }
    public string Sku { get; private set; } = default!;
    public string ItemName { get; private set; } = default!;
    public string Unit { get; private set; } = default!;
    public Quantity Quantity { get; private set; }
    public decimal ReceivedQuantity { get; private set; }
    public Money UnitCost { get; private set; }
    public Money LineTotal => UnitCost * Quantity;
    public decimal Outstanding => Quantity.Value - ReceivedQuantity;

    private PurchaseOrderLine() { }

    public PurchaseOrderLine(ItemId itemId, string sku, string itemName, string unit, Quantity quantity, Money unitCost)
    { ItemId = itemId; Sku = sku; ItemName = itemName; Unit = unit; Quantity = quantity; UnitCost = unitCost; }

    public void EnsureCanReceive(decimal quantity)
    {
        if (quantity > Outstanding)
            throw new DomainException($"Only {Outstanding:0.###} {Unit} of {Sku} is still to come; {quantity:0.###} was entered.");
    }

    public void Receive(Quantity quantity)
    {
        EnsureCanReceive(quantity.Value);
        ReceivedQuantity += quantity.Value;
    }
}

internal sealed record OrderLineSpec(ItemId ItemId, string Sku, string Name, string Unit, bool AllowsFractions, bool IsActive,
    Quantity Quantity, Money UnitCost);

internal sealed class PurchaseOrder : Entity<PurchaseOrderId>
{
    private readonly List<PurchaseOrderLine> _lines = new();
    public IReadOnlyList<PurchaseOrderLine> Lines => _lines;

    public string Number { get; private set; } = default!;
    public SupplierId SupplierId { get; private set; }
    public PurchaseOrderStatus Status { get; private set; }
    public DateOnly? ExpectedDate { get; private set; }
    public string? Notes { get; private set; }
    public UserId CreatedBy { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public UserId? ApprovedBy { get; private set; }
    public DateTime? ApprovedUtc { get; private set; }
    public DateTime? SentUtc { get; private set; }

    public Money Total => _lines.Aggregate(Money.Zero, (sum, l) => sum + l.LineTotal);

    private PurchaseOrder() { }

    public static PurchaseOrder Create(string number, Supplier supplier, UserId createdBy, DateTime nowUtc)
    {
        supplier.EnsureCanOrderFrom();
        return new PurchaseOrder
        {
            Number = number, SupplierId = supplier.Id, CreatedBy = createdBy, CreatedUtc = nowUtc, Status = PurchaseOrderStatus.Draft
        };
    }

    public void Describe(DateOnly? expectedDate, string? notes)
    {
        EnsureStatus(PurchaseOrderStatus.Draft);
        ExpectedDate = expectedDate;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public void SetLines(IReadOnlyList<OrderLineSpec> lines)
    {
        EnsureStatus(PurchaseOrderStatus.Draft);
        if (lines.Count == 0) throw new DomainException("A purchase order needs at least one line.");
        if (lines.GroupBy(l => l.ItemId).Any(g => g.Count() > 1))
            throw new DomainException("Each item may appear on a purchase order only once.");

        foreach (var l in lines)
        {
            if (!l.IsActive) throw new DomainException($"Item {l.Sku} is inactive.");
            if (!l.AllowsFractions && !l.Quantity.IsWhole)
                throw new DomainException($"Item {l.Sku} is counted in whole {l.Unit}; {l.Quantity} is not allowed.");
        }
        // Checked first, replaced after: a refused line leaves the order exactly as it was.
        _lines.Clear();
        _lines.AddRange(lines.Select(l => new PurchaseOrderLine(l.ItemId, l.Sku, l.Name, l.Unit, l.Quantity, l.UnitCost)));
    }

    // Spending needs a second pair of eyes: nobody approves their own purchase order.
    public void Approve(UserId approver, DateTime nowUtc)
    {
        EnsureStatus(PurchaseOrderStatus.Draft);
        if (_lines.Count == 0) throw new DomainException("A purchase order needs at least one line.");
        if (approver == CreatedBy) throw new DomainException("A purchase order can't be approved by the person who created it.");
        Status = PurchaseOrderStatus.Approved;
        ApprovedBy = approver;
        ApprovedUtc = nowUtc;
    }

    public void MarkSent(DateTime nowUtc)
    {
        EnsureStatus(PurchaseOrderStatus.Approved);
        Status = PurchaseOrderStatus.Sent;
        SentUtc = nowUtc;
    }

    // Returns what to put into stock. Goods can arrive before we mark the order sent, so Approved counts too.
    public IReadOnlyList<(ItemId ItemId, Quantity Quantity)> Receive(IReadOnlyList<(long LineId, Quantity Quantity)> received)
    {
        EnsureStatus(PurchaseOrderStatus.Approved, PurchaseOrderStatus.Sent, PurchaseOrderStatus.PartiallyReceived);
        if (received.Count == 0) throw new DomainException("Enter at least one received quantity.");

        var receipt = received
            .Select(r => (Line: _lines.SingleOrDefault(l => l.Id == r.LineId)
                ?? throw new DomainException($"Line {r.LineId} is not on {Number}."), r.Quantity))
            .ToList();
        // Every line is checked before any changes, so a refused line leaves the whole order as it was.
        foreach (var sameLine in receipt.GroupBy(r => r.Line))
            sameLine.Key.EnsureCanReceive(sameLine.Sum(r => r.Quantity.Value));

        var toStock = new List<(ItemId, Quantity)>();
        foreach (var (line, quantity) in receipt)
        {
            line.Receive(quantity);
            toStock.Add((line.ItemId, quantity));
        }

        Status = _lines.All(l => l.Outstanding == 0) ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartiallyReceived;
        return toStock;
    }

    public void Close()
    {
        EnsureStatus(PurchaseOrderStatus.PartiallyReceived);
        Status = PurchaseOrderStatus.Closed;
    }

    public void Cancel()
    {
        EnsureStatus(PurchaseOrderStatus.Draft, PurchaseOrderStatus.Approved, PurchaseOrderStatus.Sent);
        Status = PurchaseOrderStatus.Cancelled;
    }

    private void EnsureStatus(params PurchaseOrderStatus[] allowed)
    {
        if (!allowed.Contains(Status))
            throw new DomainException($"{Number} is {Status}; that can't be done now.");
    }
}
