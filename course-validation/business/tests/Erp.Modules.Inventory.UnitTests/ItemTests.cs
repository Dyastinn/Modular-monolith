using AwesomeAssertions;
using Erp.Modules.Inventory.Contracts;
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
        item.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ItemCatalogChanged>()
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

        var shipment = Shipment.Record(new ShipmentId(1), "SH-1", 7, "SO-7", [new ShipmentLine(new ItemId(1), 2)], Clerk, Now);
        shipment.DomainEvents.Should().ContainSingle().Which.Should().Be(new ShipmentRecorded(1, "SH-1", 7, Now));
    }
}
