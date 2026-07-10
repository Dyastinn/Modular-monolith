using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;
using FluentAssertions;

namespace Erp.Modules.Sales.UnitTests;

// Phase 12 §3 — the business rules, tested with no database. These should be the bulk of the tests.
public class CustomerRuleTests
{
    [Fact]
    public void Blocked_customer_cannot_order()
    {
        var c = new Customer(new CustomerId(3), "Gamma", isBlocked: true, new Money(20_000), Money.Zero);
        c.Invoking(x => x.EnsureNotBlocked()).Should().Throw<DomainException>().WithMessage("*blocked*");
    }

    [Fact]
    public void Order_over_available_credit_is_rejected()
    {
        var c = new Customer(new CustomerId(2), "Beta", false, new Money(5000), new Money(4500));
        c.Invoking(x => x.EnsureWithinCreditLimit(new Money(600)))   // 4500 + 600 = 5100 > 5000
         .Should().Throw<DomainException>().WithMessage("*credit limit*");
    }

    [Fact]
    public void Order_within_available_credit_is_allowed()
    {
        var c = new Customer(new CustomerId(2), "Beta", false, new Money(5000), new Money(4500));
        c.Invoking(x => x.EnsureWithinCreditLimit(new Money(400))).Should().NotThrow();   // 4900 <= 5000
    }
}

public class CategoryRuleTests
{
    [Fact]
    public void Controlled_category_requires_manager_role()
    {
        var controlled = new ItemCategory(2, "Controlled", maxQuantityPerOrder: 5, requiredRole: "SalesManager");
        controlled.Invoking(c => c.EnsureRoleAllowed("SalesRep")).Should().Throw<DomainException>();
        controlled.Invoking(c => c.EnsureRoleAllowed("SalesManager")).Should().NotThrow();
        controlled.Invoking(c => c.EnsureRoleAllowed("Admin")).Should().NotThrow();   // admin override
    }

    [Fact]
    public void Category_quantity_cap_is_enforced()
    {
        var controlled = new ItemCategory(2, "Controlled", maxQuantityPerOrder: 5, requiredRole: "SalesManager");
        controlled.Invoking(c => c.EnsureQuantityAllowed(6)).Should().Throw<DomainException>();
        controlled.Invoking(c => c.EnsureQuantityAllowed(5)).Should().NotThrow();
    }
}

public class SalesOrderStateTests
{
    [Fact]
    public void Cannot_add_lines_to_a_submitted_order()
    {
        var order = SalesOrder.Create("SO-1", new CustomerId(1), new UserId(1));
        order.AddLine(new ItemId(1), new Quantity(1), new Money(25));
        order.Submit();
        order.Invoking(o => o.AddLine(new ItemId(2), new Quantity(1), new Money(40)))
             .Should().Throw<DomainException>();
    }

    [Fact]
    public void Cannot_approve_a_draft_order()
    {
        var order = SalesOrder.Create("SO-1", new CustomerId(1), new UserId(1));
        order.AddLine(new ItemId(1), new Quantity(1), new Money(100));
        order.Invoking(o => o.Approve(new UserId(9))).Should().Throw<DomainException>();
    }

    [Fact]
    public void Cannot_submit_an_empty_order()
    {
        var order = SalesOrder.Create("SO-1", new CustomerId(1), new UserId(1));
        order.Invoking(o => o.Submit()).Should().Throw<DomainException>().WithMessage("*no lines*");
    }

    [Fact]
    public void Order_over_threshold_is_flagged_high_value()   // tests the FLAG, not a workflow branch
    {
        var order = SalesOrder.Create("SO-1", new CustomerId(1), new UserId(1));
        order.AddLine(new ItemId(1), new Quantity(1), new Money(60_000));  // > ₱50,000 threshold
        order.RequiresApproval.Should().BeTrue();
    }

    [Fact]
    public void Full_legal_lifecycle_is_allowed()
    {
        var order = SalesOrder.Create("SO-1", new CustomerId(1), new UserId(1));
        order.AddLine(new ItemId(1), new Quantity(2), new Money(25));
        order.Submit();
        order.Approve(new UserId(9));
        order.Status.Should().Be(SalesOrderStatus.Approved);
        order.ApprovedBy.Should().Be(new UserId(9));
    }

    [Fact]
    public void Approved_cannot_go_back_to_pending()
    {
        SalesOrderStatus.Approved.CanTransitionTo(SalesOrderStatus.PendingApproval).Should().BeFalse();
        SalesOrderStatus.Draft.CanTransitionTo(SalesOrderStatus.PendingApproval).Should().BeTrue();
    }

    [Fact]
    public void Total_is_the_sum_of_lines()
    {
        var order = SalesOrder.Create("SO-1", new CustomerId(1), new UserId(1));
        order.AddLine(new ItemId(1), new Quantity(2), new Money(25));   // 50
        order.AddLine(new ItemId(2), new Quantity(3), new Money(40));   // 120
        order.Total.Amount.Should().Be(170m);
    }
}

public class ValueObjectTests
{
    [Fact]
    public void Quantity_must_be_positive() =>
        FluentActions.Invoking(() => new Quantity(0)).Should().Throw<DomainException>();

    [Fact]
    public void Money_cannot_be_negative() =>
        FluentActions.Invoking(() => new Money(-1)).Should().Throw<DomainException>();
}
