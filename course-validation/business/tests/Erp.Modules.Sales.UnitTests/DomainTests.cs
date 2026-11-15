using AwesomeAssertions;
using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.UnitTests;

internal static class ShippingExtensions
{
    // Everything still to ship, in one shipment.
    public static void ShipAll(this SalesOrder order, DateTime now) =>
        order.RecordShipment(order.Lines.Where(l => l.Outstanding > 0).Select(l => (l.ItemId, l.Outstanding)).ToList(), now);
}

internal static class Make
{
    public static readonly DateTime Now = new(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);
    public static readonly UserId Rep = new(1);
    public static readonly UserId Manager = new(2);
    public static readonly UserId Accountant = new(3);
    public static readonly SalesSettings Settings = SalesSettings.Default(new Money(50_000));

    public static readonly ItemCategory General = ItemCategory.Create("General", 1000, null);
    public static readonly ItemCategory Controlled = ItemCategory.Create("Controlled", 5, Roles.SalesManager);

    public static Customer Customer(decimal limit = 1_000_000, decimal balance = 0, bool blocked = false)
    {
        var customer = Domain.Customer.Create("Acme", "123-456-789-00000", "Makati", new Money(limit), 30);
        if (balance > 0) customer.AddToBalance(new Money(balance));
        if (blocked) customer.Block();
        return customer;
    }

    public static SalesItem Item(long id, decimal price, ItemCategory? category = null, bool fractions = false, string unit = "pc",
        bool discountEligible = false)
    {
        var item = SalesItem.FromCatalog(new ItemId(id), $"SKU-{id}", $"Item {id}", unit, fractions, isActive: true);
        item.SetPricing(new Money(price), category ?? General, isForSale: true, TaxType.Vatable, discountEligible);
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
            .Should().Throw<DomainException>().WithMessage("*blocked*")
            .Which.Error.Should().Be(SalesErrors.CustomerBlocked);   // the code a person can look up

    [Fact]
    public void An_order_over_available_credit_is_not_within_the_limit() =>
        Make.Customer(limit: 5000, balance: 4500).IsWithinCreditLimit(new Money(600), Money.Zero).Should().BeFalse();

    [Fact]
    public void Orders_awaiting_approval_count_against_credit()
    {
        var customer = Make.Customer(limit: 200_000);
        customer.IsWithinCreditLimit(new Money(50_000), new Money(150_000)).Should().BeTrue();
        customer.IsWithinCreditLimit(new Money(50_000.01m), new Money(150_000)).Should().BeFalse();
    }

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
            .Invoking(i => i.SetPricing(Money.Zero, Make.General, isForSale: true, TaxType.Vatable, specialDiscountEligible: false))
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

        order.Submit(customer, Make.Settings, Money.Zero, Make.Now);

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

        order.Submit(customer, Make.Settings, Money.Zero, Make.Now);

        order.Status.Should().Be(SalesOrderStatus.PendingApproval);
        customer.OutstandingBalance.Should().Be(Money.Zero);
    }

    [Fact]
    public void Creator_cannot_approve_their_own_order()
    {
        var customer = Make.Customer();
        var order = Make.Order(customer, unitPrice: 60_000);
        order.Submit(customer, Make.Settings, Money.Zero, Make.Now);

        order.Invoking(o => o.Approve(Make.Rep, Roles.SalesManager, customer, Make.Now))
            .Should().Throw<DomainException>().WithMessage("*person who created it*");
        order.Approve(Make.Manager, Roles.SalesManager, customer, Make.Now);
        customer.OutstandingBalance.Should().Be(new Money(60_000));
    }

    [Fact]
    public void Lines_are_frozen_after_submitting()
    {
        var customer = Make.Customer();
        var order = Make.Order(customer, unitPrice: 25);
        order.Submit(customer, Make.Settings, Money.Zero, Make.Now);

        order.Invoking(o => o.SetLines([(Make.Item(2, 40), new Quantity(1))], Roles.SalesRep))
            .Should().Throw<DomainException>().WithMessage("*draft*");
    }
}

// Who says yes to what: a manager for size, accounting for credit, an admin for both, nobody for their own order.
public class ApprovalRuleTests
{
    private static (SalesOrder Order, Customer Customer) Submitted(decimal price, decimal limit = 1_000_000,
        decimal? customerThreshold = null, decimal companyThreshold = 50_000)
    {
        var customer = Customer.Create("Acme", null, null, new Money(limit), 30,
            customerThreshold is { } t ? new Money(t) : null);
        var order = Make.Order(customer, price);
        order.Submit(customer, SalesSettings.Default(new Money(companyThreshold)), Money.Zero, Make.Now);
        return (order, customer);
    }

    [Fact]
    public void The_company_limit_comes_from_settings_not_code()
    {
        Submitted(60_000, companyThreshold: 100_000).Order.Status.Should().Be(SalesOrderStatus.Approved);
        Submitted(60_000, companyThreshold: 50_000).Order.PendingApprovals.Should().Be(ApprovalNeeds.Manager);
    }

    [Fact]
    public void A_customer_limit_overrides_the_company_limit_both_ways()
    {
        Submitted(60_000, customerThreshold: 200_000).Order.Status.Should().Be(SalesOrderStatus.Approved);
        Submitted(1, customerThreshold: 0).Order.PendingApprovals.Should().Be(ApprovalNeeds.Manager);
    }

    [Fact]
    public void Over_credit_waits_for_accounting_and_nothing_is_charged_yet()
    {
        var (order, customer) = Submitted(500, limit: 100);

        order.Status.Should().Be(SalesOrderStatus.PendingApproval);
        order.PendingApprovals.Should().Be(ApprovalNeeds.CreditRelease);
        customer.OutstandingBalance.Should().Be(Money.Zero);
    }

    [Fact]
    public void A_sales_manager_cannot_release_a_credit_hold()
    {
        var (order, customer) = Submitted(500, limit: 100);

        order.Invoking(o => o.Approve(Make.Manager, Roles.SalesManager, customer, Make.Now))
            .Should().Throw<DomainException>().Which.Error.Should().Be(SalesErrors.CreditLimitExceeded);
        order.Status.Should().Be(SalesOrderStatus.PendingApproval);
    }

    [Fact]
    public void An_accountant_releases_credit_and_the_order_is_approved_and_charged()
    {
        var (order, customer) = Submitted(500, limit: 100);

        order.Approve(Make.Accountant, Roles.Accountant, customer, Make.Now);

        order.Status.Should().Be(SalesOrderStatus.Approved);
        order.CreditReleasedBy.Should().Be(Make.Accountant);
        order.ApprovedBy.Should().BeNull();   // its size never needed a manager
        customer.OutstandingBalance.Should().Be(new Money(500));
    }

    [Fact]
    public void An_accountant_cannot_approve_an_order_that_only_needs_a_manager()
    {
        var (order, customer) = Submitted(60_000);

        order.Invoking(o => o.Approve(Make.Accountant, Roles.Accountant, customer, Make.Now))
            .Should().Throw<DomainException>().Which.Error.Should().Be(SalesErrors.NotYourApproval);
    }

    [Fact]
    public void An_order_needing_both_is_approved_only_after_both_in_either_order()
    {
        var (order, customer) = Submitted(60_000, limit: 100);
        order.PendingApprovals.Should().Be(ApprovalNeeds.Manager | ApprovalNeeds.CreditRelease);

        order.Approve(Make.Manager, Roles.SalesManager, customer, Make.Now);
        order.Status.Should().Be(SalesOrderStatus.PendingApproval);
        order.PendingApprovals.Should().Be(ApprovalNeeds.CreditRelease);
        customer.OutstandingBalance.Should().Be(Money.Zero);

        order.Approve(Make.Accountant, Roles.Accountant, customer, Make.Now);
        order.Status.Should().Be(SalesOrderStatus.Approved);
        (order.ApprovedBy, order.CreditReleasedBy).Should().Be((Make.Manager, Make.Accountant));
        order.DomainEvents.Should().ContainSingle();
    }

    [Fact]
    public void An_admin_gives_both_approvals_at_once()
    {
        var (order, customer) = Submitted(60_000, limit: 100);

        order.Approve(new UserId(9), Roles.Admin, customer, Make.Now);

        order.Status.Should().Be(SalesOrderStatus.Approved);
    }

    [Fact]
    public void A_sales_rep_approves_nothing()
    {
        var (order, customer) = Submitted(60_000);

        order.Invoking(o => o.Approve(new UserId(9), Roles.SalesRep, customer, Make.Now))
            .Should().Throw<DomainException>().Which.Error.Should().Be(SalesErrors.NotYourApproval);
    }

    [Fact]
    public void Only_someone_whose_approval_is_awaited_may_reject()
    {
        var (order, _) = Submitted(500, limit: 100);

        order.Invoking(o => o.Reject(Make.Manager, Roles.SalesManager, "No", Make.Now))
            .Should().Throw<DomainException>().Which.Error.Should().Be(SalesErrors.NotYourApproval);
        order.Reject(Make.Accountant, Roles.Accountant, "Pay the overdue invoices first", Make.Now);
        order.Status.Should().Be(SalesOrderStatus.Rejected);
        order.PendingApprovals.Should().Be(ApprovalNeeds.None);
    }

    [Fact]
    public void A_negative_customer_limit_is_impossible() =>
        FluentActions.Invoking(() => Submitted(1, customerThreshold: -1)).Should().Throw<DomainException>()
            .Which.Error.Should().Be(CommonErrors.NegativeMoney);
}

// An order can leave in parts: each line tracks what has shipped, the order knows when it's done, and the rest can
// be closed when it will never come.
public class PartialShipmentTests
{
    private static (SalesOrder Order, Customer Customer) Approved(params (long Item, decimal Price, decimal Qty)[] lines)
    {
        var customer = Make.Customer();
        var order = SalesOrder.Create("SO-1", customer.Id, Make.Rep, Make.Now);
        order.SetLines(lines.Select(l => (Make.Item(l.Item, l.Price), new Quantity(l.Qty))).ToList(), Roles.SalesRep);
        order.Submit(customer, Make.Settings, Money.Zero, Make.Now);
        order.Status.Should().Be(SalesOrderStatus.Approved);
        return (order, customer);
    }

    [Fact]
    public void Part_of_a_line_ships_and_the_rest_stays_to_ship()
    {
        var (order, _) = Approved((1, 25, 10));

        order.RecordShipment([(new ItemId(1), 4)], Make.Now);

        order.Status.Should().Be(SalesOrderStatus.PartiallyShipped);
        order.Lines.Single().Outstanding.Should().Be(6);
        order.HoldsStock.Should().BeTrue("the six still to ship stay reserved");
    }

    [Fact]
    public void The_last_part_completes_the_order()
    {
        var (order, _) = Approved((1, 25, 10), (2, 40, 2));
        order.RecordShipment([(new ItemId(1), 4)], Make.Now);

        order.RecordShipment([(new ItemId(1), 6), (new ItemId(2), 2)], Make.Now);

        order.Status.Should().Be(SalesOrderStatus.Shipped);
        order.Lines.Should().OnlyContain(l => l.Outstanding == 0);
    }

    [Fact]
    public void More_than_is_still_to_ship_is_refused_and_changes_nothing()
    {
        var (order, _) = Approved((1, 25, 10));
        order.RecordShipment([(new ItemId(1), 8)], Make.Now);

        order.Invoking(o => o.RecordShipment([(new ItemId(1), 3)], Make.Now))
            .Should().Throw<DomainException>().Which.Error.Should().Be(SalesErrors.ShipmentExceedsOrder);
        order.Lines.Single().Outstanding.Should().Be(2);
    }

    [Fact]
    public void An_item_not_on_the_order_is_refused() =>
        Approved((1, 25, 10)).Order.Invoking(o => o.RecordShipment([(new ItemId(9), 1)], Make.Now))
            .Should().Throw<DomainException>().Which.Error.Should().Be(SalesErrors.ShipmentExceedsOrder);

    [Fact]
    public void Closing_short_releases_the_rest_and_takes_it_off_the_balance()
    {
        var (order, customer) = Approved((1, 25, 10), (2, 40, 2));   // ₱330 charged on approval
        order.RecordShipment([(new ItemId(1), 4)], Make.Now);          // ₱100 went out

        var released = order.CloseShort(customer);

        order.Status.Should().Be(SalesOrderStatus.Closed);
        released.Should().BeEquivalentTo([(new ItemId(1), 6m), (new ItemId(2), 2m)]);
        customer.OutstandingBalance.Should().Be(new Money(100));
        order.HoldsStock.Should().BeFalse();
    }

    [Fact]
    public void Only_a_partly_shipped_order_closes_short_and_it_cannot_be_cancelled()
    {
        var (order, customer) = Approved((1, 25, 10));
        order.Invoking(o => o.CloseShort(customer)).Should().Throw<DomainException>("nothing has shipped yet: cancel instead");

        order.RecordShipment([(new ItemId(1), 4)], Make.Now);
        order.Invoking(o => o.Cancel(customer)).Should().Throw<DomainException>("goods already left: close short instead");
    }

    [Fact]
    public void A_discounted_order_ships_in_one_go()
    {
        var customer = Make.Customer();
        var order = SalesOrder.Create("SO-1", customer.Id, Make.Rep, Make.Now);
        order.SetLines([(Make.Item(1, 112, discountEligible: true), new Quantity(2))], Roles.SalesRep);
        order.ApplySpecialDiscount(SpecialDiscount.Grant("Senior citizen", "OSCA ID No.", 0.20m, vatExempt: true, "Lola Basyang", "OSCA-1",
            new TaxPolicy(TaxRegime.Vat, 0.12m)));
        order.Submit(customer, Make.Settings, Money.Zero, Make.Now);

        order.Invoking(o => o.RecordShipment([(new ItemId(1), 1)], Make.Now))
            .Should().Throw<DomainException>().Which.Error.Should().Be(SalesErrors.DiscountedOrderShipsWhole);
        order.RecordShipment([(new ItemId(1), 2)], Make.Now);
        order.Status.Should().Be(SalesOrderStatus.Shipped);
    }
}

public class ShipRejectCancelTests
{
    [Fact]
    public void Only_an_approved_order_ships_and_a_shipped_order_cannot_be_cancelled()
    {
        var customer = Make.Customer();
        var order = Make.Order(customer, unitPrice: 60_000);
        order.Invoking(o => o.ShipAll(Make.Now)).Should().Throw<DomainException>();

        order.Submit(customer, Make.Settings, Money.Zero, Make.Now);
        order.Approve(Make.Manager, Roles.SalesManager, customer, Make.Now);
        order.ShipAll(Make.Now);

        order.Status.Should().Be(SalesOrderStatus.Shipped);
        order.Invoking(o => o.Cancel(customer)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Rejecting_needs_a_reason_and_releases_the_stock()
    {
        var customer = Make.Customer();
        var order = Make.Order(customer, unitPrice: 60_000);
        order.Submit(customer, Make.Settings, Money.Zero, Make.Now);

        order.Invoking(o => o.Reject(Make.Manager, Roles.SalesManager, " ", Make.Now)).Should().Throw<DomainException>();
        order.Reject(Make.Manager, Roles.SalesManager, "Customer asked for a quote first", Make.Now).Should().HaveCount(1);
        order.Status.Should().Be(SalesOrderStatus.Rejected);
    }

    [Fact]
    public void Cancelling_an_approved_order_reverses_the_charge_and_releases_stock()
    {
        var customer = Make.Customer();
        var order = Make.Order(customer, unitPrice: 25, qty: 2);
        order.Submit(customer, Make.Settings, Money.Zero, Make.Now);

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

public class SpecialDiscountTests
{
    private static readonly TaxPolicy Vat12 = new(TaxRegime.Vat, 0.12m);

    private static SpecialDiscount Senior(TaxPolicy? tax = null, string idNumber = "OSCA-0001") =>
        SpecialDiscount.Grant("Senior citizen", "OSCA ID No.", 0.20m, vatExempt: true, "Lola Basyang", idNumber, tax ?? Vat12);

    private static SalesOrder Draft(Customer customer, params (SalesItem Item, decimal Qty)[] lines)
    {
        var order = SalesOrder.Create("SO-1", customer.Id, Make.Rep, Make.Now);
        order.SetLines(lines.Select(l => (l.Item, new Quantity(l.Qty))).ToList(), Roles.SalesRep);
        return order;
    }

    [Fact]
    public void A_senior_pays_no_VAT_and_then_20_percent_less_on_eligible_items_only()
    {
        var order = Draft(Make.Customer(), (Make.Item(1, 1120, discountEligible: true), 1), (Make.Item(2, 112), 1));

        order.ApplySpecialDiscount(Senior());

        order.SpecialDiscountVatExemption.Should().Be(new Money(120));
        order.SpecialDiscountAmount.Should().Be(new Money(200));
        order.Total.Should().Be(new Money(912));   // ₱1,120 → ₱1,000 → ₱800, plus the ₱112 item at full price
    }

    [Fact]
    public void A_non_VAT_seller_gives_the_discount_without_a_VAT_exemption()
    {
        var order = Draft(Make.Customer(), (Make.Item(1, 1120, discountEligible: true), 1));

        order.ApplySpecialDiscount(Senior(new TaxPolicy(TaxRegime.PercentageTax, 0.12m)));

        order.SpecialDiscountVatExemption.Should().Be(Money.Zero);
        order.Total.Should().Be(new Money(896));
    }

    [Fact]
    public void Changing_the_lines_recalculates_the_discount_and_removing_it_restores_the_total()
    {
        var medicine = Make.Item(1, 1120, discountEligible: true);
        var order = Draft(Make.Customer(), (medicine, 1));
        order.ApplySpecialDiscount(Senior());

        order.SetLines([(medicine, new Quantity(2))], Roles.SalesRep);
        order.Total.Should().Be(new Money(1600));

        order.RemoveSpecialDiscount();
        order.Total.Should().Be(new Money(2240));
    }

    [Fact]
    public void A_discount_needs_an_ID_an_eligible_item_and_a_draft_order()
    {
        FluentActions.Invoking(() => Senior(idNumber: " ")).Should().Throw<DomainException>().WithMessage("*OSCA ID No.*");

        Draft(Make.Customer(), (Make.Item(2, 112), 1))
            .Invoking(o => o.ApplySpecialDiscount(Senior())).Should().Throw<DomainException>().WithMessage("*qualifies*");

        var customer = Make.Customer();
        var submitted = Draft(customer, (Make.Item(1, 1120, discountEligible: true), 1));
        submitted.Submit(customer, Make.Settings, Money.Zero, Make.Now);
        submitted.Invoking(o => o.ApplySpecialDiscount(Senior())).Should().Throw<DomainException>().WithMessage("*draft*");
    }

    [Fact]
    public void A_program_rate_is_a_fraction_and_a_withdrawn_program_grants_nothing()
    {
        FluentActions.Invoking(() => SpecialDiscountProgram.Define("SP", "Solo parent", "Solo Parent ID No.", 10m, vatExempt: true))
            .Should().Throw<DomainException>().WithMessage("*fraction*");

        var program = SpecialDiscountProgram.Define("sp", "Solo parent", "Solo Parent ID No.", 0.10m, vatExempt: true);
        program.Code.Should().Be("SP");
        program.Update(program.Name, program.IdLabel, program.Rate, program.VatExempt, isActive: false);
        program.Invoking(p => p.Grant("Juana", "SP-1", Vat12)).Should().Throw<DomainException>().WithMessage("*no longer offered*");
    }
}
