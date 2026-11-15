using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;

namespace Erp.Integration.Tests;

// Every list sorts on the server, by an allow-listed column; the SQL never builds an ORDER BY from the text.
[Collection("Integration")]
public class ListSortingTests(CustomWebAppFactory factory)
{
    private sealed record Row(long Id, decimal Total, string OrderNumber);
    private sealed record CustomerRow(long Id, string Name, decimal CreditLimit);
    private sealed record Page<T>(List<T> Items, int TotalCount);

    [Fact]
    public async Task Orders_sort_by_total_both_ways()
    {
        var manager = await factory.ClientFor("manager@erp.local");

        var up = (await manager.GetFromJsonAsync<Page<Row>>("/api/sales-orders?sort=total&pageSize=100"))!.Items;
        var down = (await manager.GetFromJsonAsync<Page<Row>>("/api/sales-orders?sort=total&desc=true&pageSize=100"))!.Items;

        up.Should().HaveCountGreaterThan(1);
        up.Select(r => r.Total).Should().BeInAscendingOrder();
        down.Select(r => r.Total).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task Customers_sort_by_credit_limit()
    {
        var manager = await factory.ClientFor("manager@erp.local");

        var rows = (await manager.GetFromJsonAsync<Page<CustomerRow>>("/api/customers?sort=creditLimit&desc=true&pageSize=100"))!.Items;

        rows.Select(r => r.CreditLimit).Should().BeInDescendingOrder();
    }

    [Theory]
    [InlineData("/api/sales-orders?sort=total;DROP TABLE sales.customer")]
    [InlineData("/api/sales-orders?sort=Total")]            // the names are exact, the way the JSON writes them
    [InlineData("/api/customers?sort=tin")]                 // a real column, but not one the list sorts by
    [InlineData("/api/purchase-orders?sort=createdBy")]
    [InlineData("/api/price-list?sort=unit")]
    [InlineData("/api/suppliers?sort=email")]
    [InlineData("/api/users?sort=password")]
    public async Task A_sort_the_list_does_not_offer_is_a_400(string url)
    {
        var admin = await factory.ClientFor("admin@erp.local");
        (await admin.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/api/sales-orders?sort=customerName&desc=true")]
    [InlineData("/api/purchase-orders?sort=supplierName")]
    [InlineData("/api/price-list?sort=unitPrice&desc=true")]
    [InlineData("/api/suppliers?sort=paymentTermsDays")]
    [InlineData("/api/users?sort=lastSignIn&desc=true")]
    public async Task Every_offered_sort_runs(string url)
    {
        var admin = await factory.ClientFor("admin@erp.local");
        (await admin.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
