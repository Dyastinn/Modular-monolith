using AwesomeAssertions;
using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.UnitTests;

// Bugs live at the edges: exactly at a limit, one centavo past it, zero, the smallest step.
public class MoneyAndQuantityBoundaryTests
{
    [Theory]
    [InlineData("0.005", "0.01")]   // half a centavo rounds up, away from zero
    [InlineData("0.004", "0.00")]
    [InlineData("1.005", "1.01")]
    [InlineData("2.675", "2.68")]   // the classic double-rounding trap; decimal gets it right
    [InlineData("0", "0.00")]
    public void Money_rounds_half_a_centavo_away_from_zero(string amount, string expected) =>
        new Money(decimal.Parse(amount)).Amount.Should().Be(decimal.Parse(expected));

    [Fact]
    public void Taking_away_more_money_than_there_is_fails() =>
        FluentActions.Invoking(() => new Money(5) - new Money(5.01m)).Should().Throw<DomainException>();

    [Fact]
    public void The_largest_amount_the_database_holds_is_still_money() =>
        new Money(9_999_999_999_999_999.99m).Amount.Should().Be(9_999_999_999_999_999.99m);   // numeric(18,2)

    [Theory]
    [InlineData("0.001")]
    [InlineData("1")]
    [InlineData("999999.999")]
    public void The_smallest_and_largest_sensible_quantities_are_accepted(string value) =>
        new Quantity(decimal.Parse(value)).Value.Should().Be(decimal.Parse(value));

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("-0.001")]
    [InlineData("0.0001")]
    [InlineData("1.0005")]
    public void A_quantity_that_is_not_positive_or_has_four_decimals_is_refused(string value) =>
        FluentActions.Invoking(() => new Quantity(decimal.Parse(value))).Should().Throw<DomainException>();

    [Fact]
    public void Trailing_zeros_do_not_count_as_decimals() =>
        new Quantity(2.5000m).IsWhole.Should().BeFalse();
}

public class LimitBoundaryTests
{
    [Theory]
    [InlineData(0, true)]      // cash on delivery
    [InlineData(365, true)]
    [InlineData(-1, false)]
    [InlineData(366, false)]
    public void Payment_terms_run_from_0_to_365_days(int days, bool allowed)
    {
        var create = () => Customer.Create("Acme", null, null, Money.Zero, days);
        if (allowed) create.Should().NotThrow();
        else create.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_customer_needs_a_name(string name) =>
        FluentActions.Invoking(() => Customer.Create(name, null, null, Money.Zero, 30)).Should().Throw<DomainException>();

    [Fact]
    public void Credit_up_to_exactly_the_limit_is_allowed() =>
        Make.Customer(limit: 5000, balance: 4500).Invoking(c => c.EnsureWithinCreditLimit(new Money(500), Money.Zero))
            .Should().NotThrow();

    [Fact]
    public void One_centavo_over_the_limit_is_refused() =>
        Make.Customer(limit: 5000, balance: 4500).Invoking(c => c.EnsureWithinCreditLimit(new Money(500.01m), Money.Zero))
            .Should().Throw<DomainException>().WithMessage("*Available credit: ₱500.00*");

    [Fact]
    public void A_customer_already_over_the_limit_shows_zero_available_not_a_negative() =>
        Make.Customer(limit: 1000, balance: 1500).Invoking(c => c.EnsureWithinCreditLimit(new Money(1), Money.Zero))
            .Should().Throw<DomainException>().WithMessage("*Available credit: ₱0.00*");

    [Fact]
    public void Exactly_the_category_cap_is_allowed()
    {
        var order = SalesOrder.Create("SO-1", new CustomerId(0), Make.Manager, Make.Now);
        order.SetLines([(Make.Item(3, 500, Make.Controlled), new Quantity(5))], Roles.SalesManager);
        order.Lines.Single().Quantity.Should().Be(new Quantity(5));
    }

    [Fact]
    public void The_same_item_twice_counts_toward_the_cap()
    {
        var order = SalesOrder.Create("SO-1", new CustomerId(0), Make.Manager, Make.Now);
        var kit = Make.Item(3, 500, Make.Controlled);

        order.Invoking(o => o.SetLines([(kit, new Quantity(3)), (kit, new Quantity(3))], Roles.SalesManager))
            .Should().Throw<DomainException>().WithMessage("*at most 5*requested 6*");
    }

    [Fact]
    public void An_order_of_exactly_50000_is_approved_on_submit()
    {
        var customer = Make.Customer();
        var order = Make.Order(customer, unitPrice: 50_000);

        order.Submit(customer, Money.Zero, Make.Now);

        order.Status.Should().Be(SalesOrderStatus.Approved);
    }

    [Fact]
    public void One_centavo_over_50000_waits_for_a_manager()
    {
        var customer = Make.Customer();
        var order = Make.Order(customer, unitPrice: 50_000.01m);

        order.Submit(customer, Money.Zero, Make.Now);

        order.Status.Should().Be(SalesOrderStatus.PendingApproval);
    }

    [Fact]
    public void An_order_needs_at_least_one_line() =>
        SalesOrder.Create("SO-1", new CustomerId(0), Make.Rep, Make.Now)
            .Invoking(o => o.SetLines([], Roles.SalesRep))
            .Should().Throw<DomainException>().WithMessage("*at least one line*");

    [Fact]
    public void An_admin_may_sell_controlled_items()
    {
        var order = SalesOrder.Create("SO-1", new CustomerId(0), new UserId(3), Make.Now);
        order.Invoking(o => o.SetLines([(Make.Item(3, 500, Make.Controlled), new Quantity(1))], Roles.Admin))
            .Should().NotThrow();
    }

    [Fact]
    public void A_failed_change_of_lines_keeps_the_old_lines()
    {
        var order = Make.Order(Make.Customer(), unitPrice: 25, qty: 2);

        order.Invoking(o => o.SetLines([(Make.Item(3, 500, Make.Controlled), new Quantity(1))], Roles.SalesRep))
            .Should().Throw<DomainException>();

        order.Lines.Should().ContainSingle().Which.Quantity.Should().Be(new Quantity(2));
    }
}

// Every status change that isn't on the list must fail, and leave the order and the customer as they were.
public class StatusTransitionTests
{
    public enum Step { Submit, Approve, Reject, Cancel, Ship }

    private static (SalesOrder Order, Customer Customer) OrderIn(SalesOrderStatus status)
    {
        var customer = Make.Customer();
        var order = Make.Order(customer, unitPrice: 60_000);   // over the threshold, so it waits for a manager
        if (status == SalesOrderStatus.Draft) return (order, customer);

        order.Submit(customer, Money.Zero, Make.Now);
        switch (status)
        {
            case SalesOrderStatus.Approved: order.Approve(Make.Manager, customer, Make.Now); break;
            case SalesOrderStatus.Rejected: order.Reject(Make.Manager, "No", Make.Now); break;
            case SalesOrderStatus.Cancelled: order.Cancel(customer); break;
            case SalesOrderStatus.Shipped: order.Approve(Make.Manager, customer, Make.Now); order.MarkShipped(Make.Now); break;
        }
        return (order, customer);
    }

    private static void Do(Step step, SalesOrder order, Customer customer)
    {
        switch (step)
        {
            case Step.Submit: order.Submit(customer, Money.Zero, Make.Now); break;
            case Step.Approve: order.Approve(Make.Manager, customer, Make.Now); break;
            case Step.Reject: order.Reject(Make.Manager, "No", Make.Now); break;
            case Step.Cancel: order.Cancel(customer); break;
            case Step.Ship: order.MarkShipped(Make.Now); break;
        }
    }

    [Theory]
    [InlineData("Draft", Step.Approve)]
    [InlineData("Draft", Step.Reject)]
    [InlineData("Draft", Step.Ship)]
    [InlineData("PendingApproval", Step.Submit)]
    [InlineData("PendingApproval", Step.Ship)]
    [InlineData("Approved", Step.Submit)]
    [InlineData("Approved", Step.Approve)]
    [InlineData("Approved", Step.Reject)]
    [InlineData("Rejected", Step.Approve)]
    [InlineData("Rejected", Step.Cancel)]
    [InlineData("Cancelled", Step.Submit)]
    [InlineData("Cancelled", Step.Cancel)]
    [InlineData("Shipped", Step.Cancel)]
    [InlineData("Shipped", Step.Ship)]
    public void A_step_not_allowed_from_a_status_is_refused_and_changes_nothing(string status, Step step)
    {
        var from = Enum.Parse<SalesOrderStatus>(status);   // the status enum is internal; a public test can't take it
        var (order, customer) = OrderIn(from);
        var balance = customer.OutstandingBalance;

        order.Invoking(o => Do(step, o, customer)).Should().Throw<DomainException>();

        order.Status.Should().Be(from);
        customer.OutstandingBalance.Should().Be(balance);
    }

    [Fact]
    public void Cancelling_an_order_awaiting_approval_releases_stock_but_reverses_no_charge()
    {
        var (order, customer) = OrderIn(SalesOrderStatus.PendingApproval);

        order.Cancel(customer).Should().HaveCount(1);
        customer.OutstandingBalance.Should().Be(Money.Zero);
    }

    [Fact]
    public void Cancelling_after_a_payment_never_takes_the_balance_below_zero()
    {
        var (order, customer) = OrderIn(SalesOrderStatus.Approved);
        customer.DeductFromBalance(new Money(59_000));   // most of it already paid

        order.Cancel(customer);

        customer.OutstandingBalance.Should().Be(Money.Zero);
    }

    [Fact]
    public void A_rejection_reason_is_kept_without_its_surrounding_spaces()
    {
        var (order, _) = OrderIn(SalesOrderStatus.PendingApproval);
        order.Reject(Make.Manager, "  Price not agreed  ", Make.Now);
        order.RejectionReason.Should().Be("Price not agreed");
    }
}
