using AwesomeAssertions;
using Erp.Modules.Purchasing.Domain;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Purchasing.UnitTests;

public class PurchaseOrderTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly UserId Buyer = new(5);
    private static readonly UserId Admin = new(3);
    private static readonly PurchasingSettings EveryOrder = PurchasingSettings.Default(Money.Zero);   // an admin approves all

    private static OrderLineSpec Line(long item, decimal qty, decimal cost, bool fractions = false, bool active = true) =>
        new(new ItemId(item), $"SKU-{item}", $"Item {item}", fractions ? "kg" : "pc", fractions, active, new Quantity(qty), new Money(cost));

    private static PurchaseOrder Draft(params OrderLineSpec[] lines)
    {
        var po = PurchaseOrder.Create("PO-1", Supplier.Create("Manila Hardware", null, null, null, null, 30), Buyer, Now);
        po.SetLines(lines.Length > 0 ? lines : [Line(1, 10, 300), Line(5, 20.5m, 410, fractions: true)]);
        return po;
    }

    [Fact]
    public void An_inactive_supplier_gets_no_orders()
    {
        var supplier = Supplier.Create("Old Supplier", null, null, null, null, 30);
        supplier.SetActive(false);
        FluentActions.Invoking(() => PurchaseOrder.Create("PO-1", supplier, Buyer, Now)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Lines_check_each_item()
    {
        var po = PurchaseOrder.Create("PO-1", Supplier.Create("S", null, null, null, null, 0), Buyer, Now);
        po.Invoking(p => p.SetLines([Line(1, 1.5m, 10)])).Should().Throw<DomainException>().WithMessage("*whole*");
        po.Invoking(p => p.SetLines([Line(1, 1, 10, active: false)])).Should().Throw<DomainException>().WithMessage("*inactive*");
        po.Invoking(p => p.SetLines([Line(1, 1, 10), Line(1, 2, 10)])).Should().Throw<DomainException>().WithMessage("*once*");
    }

    [Fact]
    public void The_buyer_cannot_approve_their_own_order()
    {
        var po = Draft();
        po.Submit(EveryOrder, Now);
        po.Invoking(p => p.Approve(Buyer, Now)).Should().Throw<DomainException>();
        po.Approve(Admin, Now);
        po.Status.Should().Be(PurchaseOrderStatus.Approved);
        po.Total.Should().Be(new Money(3000 + 8405));
    }

    [Fact]
    public void Receiving_in_parts_tracks_what_is_still_to_come()
    {
        // Line ids come from the database (0 in memory), so this uses a single-line order.
        var single = Draft(Line(1, 10, 300));
        single.Submit(EveryOrder, Now);
        single.Approve(Admin, Now);
        single.MarkSent(Now);
        var toStock = single.Receive([(0, new Quantity(4))]);

        toStock.Should().ContainSingle().Which.Quantity.Should().Be(new Quantity(4));
        single.Status.Should().Be(PurchaseOrderStatus.PartiallyReceived);
        single.Invoking(p => p.Receive([(0, new Quantity(7))])).Should().Throw<DomainException>().WithMessage("*still to come*");

        single.Receive([(0, new Quantity(6))]);
        single.Status.Should().Be(PurchaseOrderStatus.Received);
    }

    [Fact]
    public void A_draft_cannot_be_received_and_a_received_order_cannot_be_cancelled()
    {
        var po = Draft(Line(1, 2, 300));
        po.Invoking(p => p.Receive([(0, new Quantity(1))])).Should().Throw<DomainException>();

        po.Submit(EveryOrder, Now);

        po.Approve(Admin, Now);
        po.Receive([(0, new Quantity(2))]);
        po.Invoking(p => p.Cancel()).Should().Throw<DomainException>();
    }

    [Fact]
    public void A_partly_received_order_can_be_closed_short()
    {
        var po = Draft(Line(1, 10, 300));
        po.Submit(EveryOrder, Now);
        po.Approve(Admin, Now);
        po.Receive([(0, new Quantity(4))]);
        po.Close();
        po.Status.Should().Be(PurchaseOrderStatus.Closed);
    }

    [Fact]
    public void A_refused_receipt_leaves_the_order_exactly_as_it_was()
    {
        var po = Draft(Line(1, 10, 300));
        po.Submit(EveryOrder, Now);
        po.Approve(Admin, Now);

        // The same line twice: 4 fits, but 4 + 7 is more than the 10 ordered.
        po.Invoking(p => p.Receive([(0, new Quantity(4)), (0, new Quantity(7))])).Should().Throw<DomainException>();

        po.Lines.Single().Outstanding.Should().Be(10);
        po.Status.Should().Be(PurchaseOrderStatus.Approved);
    }

    [Fact]
    public void A_refused_line_leaves_the_old_lines()
    {
        var po = Draft(Line(1, 10, 300));
        po.Invoking(p => p.SetLines([Line(2, 1, 10), Line(3, 1, 10, active: false)])).Should().Throw<DomainException>();
        po.Lines.Should().ContainSingle().Which.ItemId.Should().Be(new ItemId(1));
    }

    [Fact]
    public void A_draft_must_be_submitted_before_anyone_approves_it() =>
        Draft().Invoking(p => p.Approve(Admin, Now)).Should().Throw<DomainException>()
            .Which.Error.Should().Be(PurchasingErrors.InvalidStatusChange);

    [Fact]
    public void Up_to_the_approval_limit_an_order_is_approved_on_submit()
    {
        var po = Draft(Line(1, 10, 300));   // ₱3,000

        po.Submit(PurchasingSettings.Default(new Money(3_000)), Now);

        po.Status.Should().Be(PurchaseOrderStatus.Approved);
        po.ApprovedBy.Should().BeNull();
    }

    [Fact]
    public void One_centavo_over_the_limit_waits_for_an_admin()
    {
        var po = Draft(Line(1, 10, 300));

        po.Submit(PurchasingSettings.Default(new Money(2_999.99m)), Now);

        po.Status.Should().Be(PurchaseOrderStatus.PendingApproval);
    }

    [Fact]
    public void Sending_back_needs_a_reason_and_returns_an_editable_draft()
    {
        var po = Draft();
        po.Submit(EveryOrder, Now);

        po.Invoking(p => p.Reject(Admin, " ", Now)).Should().Throw<DomainException>()
            .Which.Error.Should().Be(PurchasingErrors.RejectionReasonRequired);
        po.Reject(Admin, "  Ask for a better price  ", Now);

        po.Status.Should().Be(PurchaseOrderStatus.Draft);
        po.RejectionReason.Should().Be("Ask for a better price");
        po.SetLines([Line(1, 10, 280)]);   // the buyer can fix it

        po.Submit(EveryOrder, Now);
        po.RejectionReason.Should().BeNull();
    }

    [Fact]
    public void An_order_waiting_for_approval_can_be_cancelled_but_not_received()
    {
        var po = Draft(Line(1, 10, 300));
        po.Submit(EveryOrder, Now);

        po.Invoking(p => p.Receive([(0, new Quantity(1))])).Should().Throw<DomainException>();
        po.Cancel();
        po.Status.Should().Be(PurchaseOrderStatus.Cancelled);
    }
}
