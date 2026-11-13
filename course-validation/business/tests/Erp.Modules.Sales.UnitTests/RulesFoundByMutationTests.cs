using System.Globalization;
using AwesomeAssertions;
using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.UnitTests;

// Stryker deleted each of these checks and every test still passed: the rule was there, but nothing proved it.
public class RulesFoundByMutationTests
{
    [Fact]
    public void A_blank_TIN_or_address_is_stored_as_nothing_and_the_rest_is_trimmed()
    {
        var customer = Customer.Create("  Acme  ", "  ", " Makati ", Money.Zero, 30);

        customer.Name.Should().Be("Acme");
        customer.Tin.Should().BeNull();
        customer.Address.Should().Be("Makati");
    }

    [Fact]
    public void An_unblocked_customer_can_order_again()
    {
        var customer = Make.Customer(blocked: true);
        customer.Unblock();
        customer.Invoking(c => c.EnsureNotBlocked()).Should().NotThrow();
    }

    [Theory]
    [InlineData(" ", 5, null)]
    [InlineData("Controlled", 0, null)]
    [InlineData("Controlled", -1, null)]
    [InlineData("Controlled", 5, "Janitor")]
    public void A_category_needs_a_name_a_positive_cap_and_a_real_role(string name, decimal cap, string? role) =>
        FluentActions.Invoking(() => ItemCategory.Create(name, cap, role)).Should().Throw<DomainException>();

    [Fact]
    public void A_category_keeps_what_it_was_created_with()
    {
        var category = ItemCategory.Create(" Controlled ", 0.5m, Roles.SalesManager);
        (category.Name, category.MaxQuantityPerOrder, category.RequiredRole).Should().Be(("Controlled", 0.5m, Roles.SalesManager));
    }

    [Fact]
    public void An_item_withdrawn_from_the_catalog_cannot_go_on_sale_or_be_ordered()
    {
        var inactive = SalesItem.FromCatalog(new ItemId(9), "OLD-1", "Old", "pc", false, isActive: false);
        inactive.Invoking(i => i.SetPricing(new Money(10), Make.General, isForSale: true, TaxType.Vatable, false))
            .Should().Throw<DomainException>().WithMessage("*inactive*");

        var withdrawn = Make.Item(9, 10);
        withdrawn.ApplyCatalog("OLD-1", "Old", "pc", false, isActive: false);
        withdrawn.Invoking(i => i.EnsureSellable()).Should().Throw<DomainException>().WithMessage("*not for sale*");
    }

    [Fact]
    public void An_item_taken_off_sale_cannot_be_ordered()
    {
        var item = Make.Item(9, 10);
        item.SetPricing(new Money(10), Make.General, isForSale: false, TaxType.Vatable, false);
        item.Invoking(i => i.EnsureSellable()).Should().Throw<DomainException>();
    }

    [Fact]
    public void A_tax_type_must_be_one_of_the_three() =>
        Make.Item(9, 10).Invoking(i => i.SetPricing(new Money(10), Make.General, true, (TaxType)7, false))
            .Should().Throw<DomainException>().WithMessage("*tax type*");

    // The handler checks these on create, but Submit is where they count: they're checked again there.
    [Fact]
    public void Submit_checks_the_block_and_the_credit_itself()
    {
        var customer = Make.Customer(limit: 100);
        var order = Make.Order(customer, unitPrice: 25);
        customer.Block();
        order.Invoking(o => o.Submit(customer, Money.Zero, Make.Now)).Should().Throw<DomainException>().WithMessage("*blocked*");

        customer.Unblock();
        order.Invoking(o => o.Submit(customer, new Money(80), Make.Now)).Should().Throw<DomainException>().WithMessage("*credit limit*");
        order.Status.Should().Be(SalesOrderStatus.Draft);
    }

    [Fact]
    public void A_new_order_with_no_lines_cannot_be_submitted()
    {
        var customer = Make.Customer();
        SalesOrder.Create("SO-1", customer.Id, Make.Rep, Make.Now)
            .Invoking(o => o.Submit(customer, Money.Zero, Make.Now)).Should().Throw<DomainException>().WithMessage("*no lines*");
    }

    // Passing another customer's record is a programming mistake, not a business rule, so it isn't a DomainException.
    [Fact]
    public void Passing_the_wrong_customer_is_a_bug_that_fails_loudly()
    {
        var customer = Make.Customer();
        var order = SalesOrder.Create("SO-1", new CustomerId(99), Make.Rep, Make.Now);
        order.SetLines([(Make.Item(1, 25), new Quantity(1))], Roles.SalesRep);

        order.Invoking(o => o.Submit(customer, Money.Zero, Make.Now)).Should().Throw<InvalidOperationException>();
        order.Invoking(o => o.Approve(Make.Manager, customer, Make.Now)).Should().Throw<InvalidOperationException>();
        order.Invoking(o => o.Cancel(customer)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void An_order_awaiting_approval_is_not_ready_to_ship()
    {
        var customer = Make.Customer();
        var order = Make.Order(customer, unitPrice: 60_000);
        order.Submit(customer, Money.Zero, Make.Now);

        order.Invoking(o => o.EnsureCanShip()).Should().Throw<DomainException>().WithMessage("*Only approved*");
    }

    [Fact]
    public void A_discount_cannot_be_removed_after_submitting()
    {
        var customer = Make.Customer();
        var order = Make.Order(customer, unitPrice: 25);
        order.Submit(customer, Money.Zero, Make.Now);

        order.Invoking(o => o.RemoveSpecialDiscount()).Should().Throw<DomainException>().WithMessage("*draft*");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    public void A_program_rate_of_nothing_or_everything_is_refused(string rate) =>
        FluentActions.Invoking(() => SpecialDiscountProgram.Define("SP", "Solo parent", "ID", decimal.Parse(rate, CultureInfo.InvariantCulture), vatExempt: false))
            .Should().Throw<DomainException>();

    [Fact]
    public void A_new_program_is_offered_straight_away() =>
        SpecialDiscountProgram.Define("SP", "Solo parent", "ID", 0.10m, vatExempt: false).IsActive.Should().BeTrue();
}
