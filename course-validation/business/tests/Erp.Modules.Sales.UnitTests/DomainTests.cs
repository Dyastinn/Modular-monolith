using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;
using Xunit;

namespace Erp.Modules.Sales.UnitTests;

public class DomainTests
{
    [Fact]
    public void Cannot_add_lines_to_a_submitted_order()
    {
        var order = SalesOrder.Create("SO-1", new CustomerId(1), new UserId(1));
        order.AddLine(new ItemId(1), new Quantity(1), new Money(25));
        order.Submit();
        Assert.Throws<DomainException>(() =>
            order.AddLine(new ItemId(2), new Quantity(1), new Money(40)));
    }

    [Fact]
    public void Order_over_threshold_is_flagged_as_high_value()
    {
        var order = SalesOrder.Create("SO-2", new CustomerId(1), new UserId(1));
        order.AddLine(new ItemId(1), new Quantity(1), new Money(15_000));
        Assert.True(order.RequiresApproval);
    }

    [Fact]
    public void Blocked_customer_is_rejected()
    {
        var c = new Customer(new CustomerId(3), "Gamma", isBlocked: true, new Money(20_000), Money.Zero);
        Assert.Throws<DomainException>(() => c.EnsureNotBlocked());
    }

    [Fact]
    public void Credit_limit_exceeded_is_rejected()
    {
        var c = new Customer(new CustomerId(2), "Beta", false, new Money(5000), new Money(4500));
        Assert.Throws<DomainException>(() => c.EnsureWithinCreditLimit(new Money(600)));
    }

    [Fact]
    public void Approved_cannot_go_back_to_pending()
    {
        Assert.False(SalesOrderStatus.Approved.CanTransitionTo(SalesOrderStatus.PendingApproval));
        Assert.True(SalesOrderStatus.Draft.CanTransitionTo(SalesOrderStatus.PendingApproval));
    }

    [Fact]
    public void Category_role_restriction_enforced()
    {
        var controlled = new ItemCategory(2, "Controlled", 5, "SalesManager");
        Assert.Throws<DomainException>(() => controlled.EnsureRoleAllowed("SalesRep"));
        controlled.EnsureRoleAllowed("SalesManager");   // ok
        controlled.EnsureRoleAllowed("Admin");          // admin override ok
    }
}
