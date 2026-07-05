using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;
using Xunit;

namespace Erp.Modules.Sales.UnitTests;

public class SalesOrderTests
{
    private static SalesOrder DraftWithLine()
    {
        var o = SalesOrder.Create(new CustomerId(1));
        o.AddLine(new ProductId(1), new Quantity(2), new Money(10m));
        return o;
    }

    [Fact]
    public void Submitting_empty_order_throws()
    {
        var o = SalesOrder.Create(new CustomerId(1));
        Assert.Throws<DomainException>(() => o.Submit(approvalGranted: false));
    }

    [Fact]
    public void Submitting_non_draft_throws()
    {
        var o = DraftWithLine();
        o.Submit(false);
        Assert.Throws<DomainException>(() => o.Submit(false));
    }

    [Fact]
    public void Over_threshold_without_approval_throws()
    {
        var o = SalesOrder.Create(new CustomerId(1));
        o.AddLine(new ProductId(1), new Quantity(1), new Money(60000m));
        Assert.True(o.RequiresApproval);
        Assert.Throws<DomainException>(() => o.Submit(approvalGranted: false));
    }

    [Fact]
    public void Money_rejects_negative()
        => Assert.Throws<DomainException>(() => new Money(-1m));

    [Fact]
    public void Quantity_rejects_zero()
        => Assert.Throws<DomainException>(() => new Quantity(0));

    [Fact]
    public void Total_is_computed_from_lines()
    {
        var o = DraftWithLine();
        Assert.Equal(20m, o.Total.Amount);
    }

    [Fact]
    public void Cancelled_order_cannot_transition()
        => Assert.False(SalesOrderStatus.Cancelled.CanTransitionTo(SalesOrderStatus.Submitted));
}
