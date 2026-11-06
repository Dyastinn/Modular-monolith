using AwesomeAssertions;
using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.UnitTests;

internal static class Make
{
    public static readonly DateTime Now = new(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);
    public static readonly UserId Rep = new(1);
    public static readonly UserId Manager = new(2);

    public static readonly ItemCategory General = ItemCategory.Create("General", 1000, null);
    public static readonly ItemCategory Controlled = ItemCategory.Create("Controlled", 5, Roles.SalesManager);

    public static Customer Customer(decimal limit = 1_000_000, decimal balance = 0, bool blocked = false)
    {
        var customer = Domain.Customer.Create("Acme", "123-456-789-00000", "Makati", new Money(limit), 30);
        if (balance > 0) customer.AddToBalance(new Money(balance));
        if (blocked) customer.Block();
        return customer;
    }

    public static SalesItem Item(long id, decimal price, ItemCategory? category = null, bool fractions = false, string unit = "pc")
    {
        var item = SalesItem.FromCatalog(new ItemId(id), $"SKU-{id}", $"Item {id}", unit, fractions, isActive: true);
        item.SetPricing(new Money(price), category ?? General, isForSale: true, TaxType.Vatable);
        return item;
    }

    public static SalesOrder Order(Customer customer, decimal unitPrice, decimal qty = 1, UserId? createdBy = null)
    {
        var order = SalesOrder.Create("SO-1", customer.Id, createdBy ?? Rep, Now);
        order.SetLines([(Item(1, unitPrice), new Quantity(qty))], Roles.SalesRep);
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
    public void Orders_awaiting_approval_count_against_credit() =>
        Make.Customer(limit: 200_000).Invoking(c => c.EnsureWithinCreditLimit(new Money(75_000), new Money(150_000)))
            .Should().Throw<DomainException>().WithMessage("*Available credit: ₱50,000.00*");

    [Fact]
    public void A_payment_never_takes_the_balance_below_zero()
    {
        var customer = Make.Customer(balance: 300);
        var applied = AppliedPayment.Apply(paymentId: 9, customer, new Money(500), Make.Now);

        customer.OutstandingBalance.Should().Be(Money.Zero);
        applied.Amount.Should().Be(new Money(300));   // only what was owed

        applied.Reverse(customer, Make.Now);
        customer.OutstandingBalance.Should().Be(new Money(300));
        applied.Reverse(customer, Make.Now);   // a repeated event changes nothing
        customer.OutstandingBalance.Should().Be(new Money(300));
    }

    [Fact]
    public void Payment_terms_must_be_sensible() =>
        FluentActions.Invoking(() => Customer.Create("Acme", null, null, Money.Zero, 400))
            .Should().Throw<DomainException>();
}

public class OrderLineTests
{
    [Fact]
    public void Controlled_items_need_a_manager()
    {
        var order = SalesOrder.Create("SO-1", new CustomerId(0), Make.Rep, Make.Now);
        var kit = Make.Item(3, 500, Make.Controlled);

        order.Invoking(o => o.SetLines([(kit, new Quantity(1))], Roles.SalesRep)).Should().Throw<DomainException>();
        order.Invoking(o => o.SetLines([(kit, new Quantity(1))], Roles.SalesManager)).Should().NotThrow();
    }

    [Fact]
    public void Category_cap_counts_across_the_whole_order()
    {
        var order = SalesOrder.Create("SO-1", new CustomerId(0), Make.Manager, Make.Now);
        var kitA = Make.Item(3, 500, Make.Controlled);
        var kitB = Make.Item(4, 500, Make.Controlled);

        order.Invoking(o => o.SetLines([(kitA, new Quantity(3)), (kitB, new Quantity(3))], Roles.SalesManager))
            .Should().Throw<DomainException>().WithMessage("*at most 5*");
    }

    [Fact]
    public void Whole_unit_items_refuse_fractions_but_bulk_items_allow_them()
    {
        var order = SalesOrder.Create("SO-1", new CustomerId(0), Make.Rep, Make.Now);

        order.Invoking(o => o.SetLines([(Make.Item(1, 25), new Quantity(1.5m))], Roles.SalesRep))
            .Should().Throw<DomainException>().WithMessage("*whole*");
        order.SetLines([(Make.Item(5, 620.50m, fractions: true, unit: "kg"), new Quantity(1.5m))], Roles.SalesRep);
        order.Total.Should().Be(new Money(930.75m));
    }

    [Fact]
    public void An_item_not_for_sale_cannot_be_ordered()
    {
        var unpriced = SalesItem.FromCatalog(new ItemId(9), "NEW-1", "New", "pc", false, isActive: true);
        SalesOrder.Create("SO-1", new CustomerId(0), Make.Rep, Make.Now)
            .Invoking(o => o.SetLines([(unpriced, new Quantity(1))], Roles.SalesRep))
            .Should().Throw<DomainException>().WithMessage("*not for sale*");
    }

    [Fact]
    public void An_item_cannot_go_on_sale_without_a_price() =>
        SalesItem.FromCatalog(new ItemId(9), "NEW-1", "New", "pc", false, isActive: true)
            .Invoking(i => i.SetPricing(Money.Zero, Make.General, isForSale: true, TaxType.Vatable))
            .Should().Throw<DomainException>().WithMessage("*price*");

    [Fact]
    public void The_same_item_twice_becomes_one_line()
    {
        var order = SalesOrder.Create("SO-1", new CustomerId(0), Make.Rep, Make.Now);
        var widget = Make.Item(1, 25);
        order.SetLines([(widget, new Quantity(2)), (widget, new Quantity(3))], Roles.SalesRep);
        order.Lines.Should().ContainSingle().Which.Quantity.Should().Be(new Quantity(5));
    }
}

public class SubmitAndApproveTests
{
    [Fact]
    public void Small_order_is_approved_automatically_and_charged()
    {
        var customer = Make.Customer();
        var order = Make.Order(customer, unitPrice: 25, qty: 2);

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
        var order = Make.Order(customer, unitPrice: 60_000);

        order.Submit(customer, Money.Zero, Make.Now);

        order.Status.Should().Be(SalesOrderStatus.PendingApproval);
        customer.OutstandingBalance.Should().Be(Money.Zero);
    }

    [Fact]
    public void Creator_cannot_approve_their_own_order()
    {
        var customer = Make.Customer();
        var order = Make.Order(customer, unitPrice: 60_000);
        order.Submit(customer, Money.Zero, Make.Now);

        order.Invoking(o => o.Approve(Make.Rep, customer, Make.Now))
            .Should().Throw<DomainException>().WithMessage("*person who created it*");
        order.Approve(Make.Manager, customer, Make.Now);
        customer.OutstandingBalance.Should().Be(new Money(60_000));
    }

    [Fact]
    public void Lines_are_frozen_after_submitting()
    {
        var customer = Make.Customer();
        var order = Make.Order(customer, unitPrice: 25);
        order.Submit(customer, Money.Zero, Make.Now);

        order.Invoking(o => o.SetLines([(Make.Item(2, 40), new Quantity(1))], Roles.SalesRep))
            .Should().Throw<DomainException>().WithMessage("*draft*");
    }
}

public class ShipRejectCancelTests
{
    [Fact]
    public void Only_an_approved_order_ships_and_a_shipped_order_cannot_be_cancelled()
    {
        var customer = Make.Customer();
        var order = Make.Order(customer, unitPrice: 60_000);
        order.Invoking(o => o.MarkShipped(Make.Now)).Should().Throw<DomainException>();

        order.Submit(customer, Money.Zero, Make.Now);
        order.Approve(Make.Manager, customer, Make.Now);
        order.MarkShipped(Make.Now);

        order.Status.Should().Be(SalesOrderStatus.Shipped);
        order.Invoking(o => o.Cancel(customer)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Rejecting_needs_a_reason_and_releases_the_stock()
    {
        var customer = Make.Customer();
        var order = Make.Order(customer, unitPrice: 60_000);
        order.Submit(customer, Money.Zero, Make.Now);

        order.Invoking(o => o.Reject(Make.Manager, " ", Make.Now)).Should().Throw<DomainException>();
        order.Reject(Make.Manager, "Customer asked for a quote first", Make.Now).Should().HaveCount(1);
        order.Status.Should().Be(SalesOrderStatus.Rejected);
    }

    [Fact]
    public void Cancelling_an_approved_order_reverses_the_charge_and_releases_stock()
    {
        var customer = Make.Customer();
        var order = Make.Order(customer, unitPrice: 25, qty: 2);
        order.Submit(customer, Money.Zero, Make.Now);

        order.Cancel(customer).Should().HaveCount(1);
        customer.OutstandingBalance.Should().Be(Money.Zero);
    }

    [Fact]
    public void Cancelling_a_draft_releases_nothing()
    {
        var customer = Make.Customer();
        Make.Order(customer, unitPrice: 25).Cancel(customer).Should().BeEmpty();
    }
}

public class VisibilityTests
{
    [Fact]
    public void A_rep_sees_only_their_own_orders_and_a_manager_sees_all()
    {
        var order = Make.Order(Make.Customer(), unitPrice: 25, createdBy: Make.Rep);

        order.IsVisibleTo(Make.Rep, Roles.SalesRep).Should().BeTrue();
        order.IsVisibleTo(new UserId(99), Roles.SalesRep).Should().BeFalse();
        order.IsVisibleTo(Make.Manager, Roles.SalesManager).Should().BeTrue();
        order.IsVisibleTo(new UserId(5), Roles.Accountant).Should().BeTrue();
    }
}

public class ValueObjectTests
{
    [Fact]
    public void Quantity_must_be_positive_with_at_most_three_decimals()
    {
        FluentActions.Invoking(() => new Quantity(0)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => new Quantity(1.2345m)).Should().Throw<DomainException>();
        new Quantity(1.234m).Value.Should().Be(1.234m);
    }

    [Fact]
    public void Money_cannot_be_negative_and_rounds_to_centavos()
    {
        FluentActions.Invoking(() => new Money(-1)).Should().Throw<DomainException>();
        (new Money(620.50m) * new Quantity(1.333m)).Should().Be(new Money(827.13m));
    }
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
