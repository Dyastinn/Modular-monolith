using AwesomeAssertions;
using Erp.Modules.Inventory.Domain;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Inventory.UnitTests;

public class ItemTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly UserId Clerk = new(4);

    private static Item Widget() => Item.Create(new ItemId(1), " gen-001 ", "Widget", "PC", allowsFractions: false);

    [Fact]
    public void A_new_item_is_tidied_and_announced()
    {
        var item = Widget();

        item.Sku.Should().Be("GEN-001");
        item.Unit.Should().Be("pc");
        item.IsActive.Should().BeTrue();
        item.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ItemChanged>()
            .Which.Name.Should().Be("Widget");
    }

    [Fact]
    public void An_adjustment_needs_a_reason_and_records_a_movement()
    {
        var item = Widget();

        item.Invoking(i => i.Adjust(10, " ", Clerk, Now)).Should().Throw<DomainException>();
        var movement = item.Adjust(10, "Opening count", Clerk, Now);

        item.OnHand.Should().Be(10);
        movement.Kind.Should().Be(MovementKind.Adjustment);
        movement.Quantity.Should().Be(10);
    }

    [Fact]
    public void Stock_cannot_be_adjusted_below_zero()
    {
        var item = Widget();
        item.Adjust(5, "Count", Clerk, Now);
        item.Invoking(i => i.Adjust(-6, "Damaged", Clerk, Now)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Whole_unit_items_refuse_fractional_adjustments()
    {
        Widget().Invoking(i => i.Adjust(1.5m, "Count", Clerk, Now)).Should().Throw<DomainException>().WithMessage("*whole*");
        Item.Create(new ItemId(5), "BLK-001", "Copper Wire", "kg", allowsFractions: true).Adjust(1.5m, "Count", Clerk, Now)
            .Quantity.Should().Be(1.5m);
    }

    [Fact]
    public void A_shipment_needs_lines_and_is_announced()
    {
        FluentActions.Invoking(() => Shipment.Record(new ShipmentId(1), "SH-1", 7, "SO-7", [], Clerk, Now))
            .Should().Throw<DomainException>();

        var line = new ShipmentLine(new ItemId(1), 2, new UnitCost(30));
        var shipment = Shipment.Record(new ShipmentId(1), "SH-1", 7, "SO-7", [line], Clerk, Now);
        var created = shipment.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ShipmentCreated>().Which;
        (created.ShipmentId, created.Number, created.SalesOrderId).Should().Be((new ShipmentId(1), "SH-1", 7L));
        created.Lines.Should().Equal(line);   // with its cost, for the invoice's margin
    }
}

// Moving-average cost: every receipt re-weights the average; what leaves takes the average with it.
public class CostingTests
{
    private static readonly DateTime Now = new(2026, 11, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly UserId Clerk = new(4);

    private static Item Widget() => Item.Create(new ItemId(1), "GEN-001", "Widget", "pc", allowsFractions: false);

    [Fact]
    public void The_first_receipt_sets_the_cost()
    {
        var item = Widget();
        var movement = item.Receive(10, new UnitCost(30), "PO-1", Clerk, Now);

        item.AverageCost.Should().Be(new UnitCost(30));
        item.StockValue.Should().Be(300);
        movement.UnitCost.Should().Be(new UnitCost(30));
    }

    [Fact]
    public void A_second_receipt_averages_by_quantity()
    {
        var item = Widget();
        item.Receive(10, new UnitCost(30), "PO-1", Clerk, Now);
        item.Receive(30, new UnitCost(34), "PO-2", Clerk, Now);   // (10×30 + 30×34) / 40 = 33

        item.AverageCost.Should().Be(new UnitCost(33));
        item.StockValue.Should().Be(1_320);
    }

    [Fact]
    public void The_average_keeps_four_decimals_so_it_does_not_drift()
    {
        var item = Widget();
        item.Receive(3, new UnitCost(10), "PO-1", Clerk, Now);
        item.Receive(1, new UnitCost(10.01m), "PO-2", Clerk, Now);   // 40.01 / 4 = 10.0025

        item.AverageCost.Value.Should().Be(10.0025m);
    }

    [Fact]
    public void Stock_found_comes_in_at_the_cost_given_and_lost_stock_leaves_at_the_average()
    {
        var item = Widget();
        item.Receive(10, new UnitCost(30), "PO-1", Clerk, Now);

        item.Adjust(10, "Found in back room", Clerk, Now, new UnitCost(20)).UnitCost.Should().Be(new UnitCost(20));
        item.AverageCost.Should().Be(new UnitCost(25));
        item.Adjust(-5, "Damaged", Clerk, Now, new UnitCost(99)).UnitCost.Should().Be(new UnitCost(25), "a loss leaves at the average");
        item.AverageCost.Should().Be(new UnitCost(25), "taking stock out never changes the average");
    }

    [Fact]
    public void After_running_out_the_next_receipt_starts_the_average_again()
    {
        var item = Widget();
        item.Receive(5, new UnitCost(30), "PO-1", Clerk, Now);
        item.Adjust(-5, "Sold off", Clerk, Now);
        item.Receive(5, new UnitCost(50), "PO-2", Clerk, Now);

        item.AverageCost.Should().Be(new UnitCost(50));
    }

    [Fact]
    public void A_negative_cost_is_refused() =>
        FluentActions.Invoking(() => new UnitCost(-0.01m)).Should().Throw<DomainException>()
            .Which.Error.Should().Be(InventoryErrors.NegativeCost);

    [Fact]
    public void A_receipt_must_add_whole_units_of_a_whole_unit_item()
    {
        Widget().Invoking(i => i.Receive(0, new UnitCost(1), "PO-1", Clerk, Now)).Should().Throw<DomainException>();
        Widget().Invoking(i => i.Receive(1.5m, new UnitCost(1), "PO-1", Clerk, Now)).Should().Throw<DomainException>().WithMessage("*whole*");
    }
}

