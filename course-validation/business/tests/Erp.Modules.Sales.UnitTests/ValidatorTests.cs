using System.Globalization;
using AwesomeAssertions;
using Erp.Modules.Sales.Features;

namespace Erp.Modules.Sales.UnitTests;

// Validators are plain classes: test the shape rules directly, no HTTP, no database.
public class CreateSalesOrderRequestValidatorTests
{
    private static readonly CreateSalesOrderRequestValidator Validator = new();

    private static CreateSalesOrderRequest Order(long customerId = 1, long itemId = 1, decimal quantity = 1) =>
        new(customerId, [new OrderLineInput(itemId, quantity)]);

    [Fact]
    public void A_well_formed_order_passes() => Validator.Validate(Order()).IsValid.Should().BeTrue();

    [Theory]
    [InlineData(0, 1, "1")]
    [InlineData(-1, 1, "1")]
    [InlineData(1, 0, "1")]
    [InlineData(1, -5, "1")]
    [InlineData(1, 1, "0")]
    [InlineData(1, 1, "-1")]
    [InlineData(1, 1, "1.2345")]
    public void A_bad_id_or_quantity_is_refused(long customerId, long itemId, string quantity) =>
        Validator.Validate(Order(customerId, itemId, decimal.Parse(quantity, CultureInfo.InvariantCulture))).IsValid.Should().BeFalse();

    [Fact]
    public void An_order_without_lines_is_refused_with_a_readable_message() =>
        Validator.Validate(new CreateSalesOrderRequest(1, [])).Errors
            .Should().ContainSingle().Which.ErrorMessage.Should().Be("An order needs at least one line.");

    [Fact]
    public void Every_bad_line_is_reported_not_just_the_first()
    {
        var result = Validator.Validate(new CreateSalesOrderRequest(1, [new(0, 1), new(1, 0), new(1, 1)]));
        result.Errors.Should().HaveCount(2);
    }
}

public class SearchSalesOrdersValidatorTests
{
    private static readonly SearchSalesOrdersValidator Validator = new();

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 100)]
    [InlineData(500, 20)]
    public void Pages_from_one_and_sizes_from_1_to_100_pass(int page, int pageSize) =>
        Validator.Validate(new SearchSalesOrdersQuery(null, null, null, page, pageSize)).IsValid.Should().BeTrue();

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void Page_zero_negative_pages_and_sizes_outside_1_to_100_are_refused(int page, int pageSize) =>
        Validator.Validate(new SearchSalesOrdersQuery(null, null, null, page, pageSize)).IsValid.Should().BeFalse();

    [Theory]
    [InlineData("Approved", true)]
    [InlineData("approved", true)]
    [InlineData("Lost", false)]
    [InlineData("1", false)]    // Enum.TryParse accepts any number, even one that is no status at all
    [InlineData("99", false)]
    [InlineData("", false)]
    public void A_status_filter_must_name_a_real_status(string status, bool valid) =>
        Validator.Validate(new SearchSalesOrdersQuery(null, status, null)).IsValid.Should().Be(valid);
}
