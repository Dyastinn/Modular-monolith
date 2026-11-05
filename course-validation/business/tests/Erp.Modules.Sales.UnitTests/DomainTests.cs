using AwesomeAssertions;
using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.UnitTests;

internal static class Make
{
    public static readonly DateTime Now = new(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);
    public static readonly UserId Rep = new(1);
    public static readonly UserId Manager = new(2);

    public static Customer Customer(decimal limit = 1_000_000, decimal balance = 0, bool blocked = false) =>
        new(new CustomerId(1), "Acme", blocked, new Money(limit), new Money(balance));

    public static SalesOrder Order(decimal unitPrice, int qty = 1)
    {
        var order = SalesOrder.Create("SO-1", new CustomerId(1), Rep, Now);
        order.AddLine(new ItemId(1), new Quantity(qty), new Money(unitPrice));
        return order;
    }
}

public class CustomerRuleTests
{
    [Fact]
    public void Blocked_customer_cannot_order() =>
        Make.Customer(blocked: true).Invoking(c => c.EnsureNotBlocked())
            .Should().Throw<DomainException>().WithMessage("*blocked*");

    [Fact]
    public void Order_over_available_credit_is_rejected() =>
        Make.Customer(limit: 5000, balance: 4500).Invoking(c => c.EnsureWithinCreditLimit(new Money(600), Money.Zero))
            .Should().Throw<DomainException>().WithMessage("*credit limit*");

    [Fact]
    public void Order_within_available_credit_is_allowed() =>
        Make.Customer(limit: 5000, balance: 4500).Invoking(c => c.EnsureWithinCreditLimit(new Money(400), Money.Zero))
            .Should().NotThrow();

    [Fact]
    public void Orders_awaiting_approval_count_against_credit() =>
        Make.Customer(limit: 200_000).Invoking(c => c.EnsureWithinCreditLimit(new Money(75_000), new Money(150_000)))
            .Should().Throw<DomainException>().WithMessage("*Available credit: ₱50,000.00*");
}

public class CategoryRuleTests
{
    private static readonly ItemCategory Controlled = new(2, "Controlled", maxQuantityPerOrder: 5, requiredRole: Roles.SalesManager);

    [Fact]
    public void Controlled_category_requires_manager_role()
    {
        Controlled.Invoking(c => c.EnsureRoleAllowed(Roles.SalesRep)).Should().Throw<DomainException>();
        Controlled.Invoking(c => c.EnsureRoleAllowed(Roles.SalesManager)).Should().NotThrow();
        Controlled.Invoking(c => c.EnsureRoleAllowed(Roles.Admin)).Should().NotThrow();
    }

    [Fact]
    public void Category_quantity_cap_is_enforced()
    {
        Controlled.Invoking(c => c.EnsureQuantityAllowed(6)).Should().Throw<DomainException>();
        Controlled.Invoking(c => c.EnsureQuantityAllowed(5)).Should().NotThrow();
    }
}

public class SubmitAndApproveTests
{
    [Fact]
    public void Small_order_is_approved_automatically_and_charged()
    {
        var customer = Make.Customer();
        var order = Make.Order(unitPrice: 25, qty: 2);

        order.Submit(customer, Money.Zero, Make.Now);

        order.Status.Should().Be(SalesOrderStatus.Approved);
        order.ApprovedBy.Should().BeNull();
        customer.OutstandingBalance.Should().Be(new Money(50));
        order.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<SalesOrderApproved>();
    }

    [Fact]
    public void High_value_order_waits_for_a_manager()
    {
        var customer = Make.Customer();
        var order = Make.Order(unitPrice: 60_000);

        order.Submit(customer, Money.Zero, Make.Now);

        order.Status.Should().Be(SalesOrderStatus.PendingApproval);
        customer.OutstandingBalance.Should().Be(Money.Zero);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Manager_approval_charges_the_customer()
    {
        var customer = Make.Customer();
        var order = Make.Order(unitPrice: 60_000);
        order.Submit(customer, Money.Zero, Make.Now);

        order.Approve(Make.Manager, customer, Make.Now);

        order.Status.Should().Be(SalesOrderStatus.Approved);
        order.ApprovedBy.Should().Be(Make.Manager);
        customer.OutstandingBalance.Should().Be(new Money(60_000));
    }

    [Fact]
    public void Creator_cannot_approve_their_own_order()
    {
        var customer = Make.Customer();
        var order = Make.Order(unitPrice: 60_000);
        order.Submit(customer, Money.Zero, Make.Now);

        order.Invoking(o => o.Approve(Make.Rep, customer, Make.Now))
            .Should().Throw<DomainException>().WithMessage("*person who created it*");
    }

    [Fact]
    public void Cannot_approve_a_draft() =>
        Make.Order(unitPrice: 60_000).Invoking(o => o.Approve(Make.Manager, Make.Customer(), Make.Now))
            .Should().Throw<DomainException>();

    [Fact]
    public void Cannot_submit_an_empty_order() =>
        SalesOrder.Create("SO-1", new CustomerId(1), Make.Rep, Make.Now)
            .Invoking(o => o.Submit(Make.Customer(), Money.Zero, Make.Now))
            .Should().Throw<DomainException>().WithMessage("*no lines*");

    [Fact]
    public void Blocked_customer_cannot_submit() =>
        Make.Order(unitPrice: 25).Invoking(o => o.Submit(Make.Customer(blocked: true), Money.Zero, Make.Now))
            .Should().Throw<DomainException>().WithMessage("*blocked*");

    [Fact]
    public void Cannot_add_lines_after_submitting()
    {
        var order = Make.Order(unitPrice: 25);
        order.Submit(Make.Customer(), Money.Zero, Make.Now);
        order.Invoking(o => o.AddLine(new ItemId(2), new Quantity(1), new Money(40))).Should().Throw<DomainException>();
    }
}

public class RejectAndCancelTests
{
    [Fact]
    public void Rejecting_needs_a_reason_and_releases_the_stock()
    {
        var order = Make.Order(unitPrice: 60_000);
        order.Submit(Make.Customer(), Money.Zero, Make.Now);

        order.Invoking(o => o.Reject(Make.Manager, " ", Make.Now)).Should().Throw<DomainException>();

        var released = order.Reject(Make.Manager, "Customer asked for a quote first", Make.Now);
        order.Status.Should().Be(SalesOrderStatus.Rejected);
        order.RejectionReason.Should().Be("Customer asked for a quote first");
        released.Should().HaveCount(1);
    }

    [Fact]
    public void Cancelling_an_approved_order_reverses_the_charge_and_releases_stock()
    {
        var customer = Make.Customer();
        var order = Make.Order(unitPrice: 25, qty: 2);
        order.Submit(customer, Money.Zero, Make.Now);   // auto-approved: charged ₱50

        var released = order.Cancel(customer);

        order.Status.Should().Be(SalesOrderStatus.Cancelled);
        customer.OutstandingBalance.Should().Be(Money.Zero);
        released.Should().HaveCount(1);
    }

    [Fact]
    public void Cancelling_a_draft_releases_nothing() =>
        Make.Order(unitPrice: 25).Cancel(Make.Customer()).Should().BeEmpty();

    [Fact]
    public void Approved_cannot_go_back_to_pending() =>
        SalesOrderStatus.Approved.CanTransitionTo(SalesOrderStatus.PendingApproval).Should().BeFalse();
}

public class ValueObjectTests
{
    [Fact]
    public void Total_is_the_sum_of_lines()
    {
        var order = SalesOrder.Create("SO-1", new CustomerId(1), Make.Rep, Make.Now);
        order.AddLine(new ItemId(1), new Quantity(2), new Money(25));
        order.AddLine(new ItemId(2), new Quantity(3), new Money(40));
        order.Total.Should().Be(new Money(170));
    }

    [Fact]
    public void Quantity_must_be_positive() =>
        FluentActions.Invoking(() => new Quantity(0)).Should().Throw<DomainException>();

    [Fact]
    public void Money_cannot_be_negative() =>
        FluentActions.Invoking(() => new Money(-1)).Should().Throw<DomainException>();
}

public class OutboxSerializationTests
{
    [Fact]
    public void An_event_survives_the_round_trip_through_the_outbox()
    {
        var original = new SalesOrderApproved(new SalesOrderId(42), new CustomerId(7), new Money(60_000.50m), new UserId(2));

        var message = OutboxMessage.From(original, Make.Now);
        var restored = OutboxMessage.Deserialize(message.Type, message.Payload);

        restored.Should().Be(original);
    }
}
