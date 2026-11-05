using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Dapper;
using Npgsql;

namespace Erp.Integration.Tests;

// Whole flows across modules, through HTTP and the outbox, the way the business uses them.
[Collection("Integration")]
public class BusinessFlowTests(CustomWebAppFactory factory)
{
    private sealed record Created(long Id);
    private sealed record Page<T>(List<T> Items, int TotalCount);
    private sealed record CustomerRow(long Id, decimal OutstandingBalance, decimal AvailableCredit);
    private sealed record InvoiceRow(long Id, string Number, decimal Total, string Status, decimal BalanceDue);
    private sealed record PriceRow(long Id, string Sku, bool IsForSale);
    private sealed record PoLine(long Id, decimal Outstanding);
    private sealed record PoDetail(string Status, List<PoLine> Lines);
    private sealed record ItemRow(long Id, decimal OnHand);
    private sealed record OrderRow(long Id);

    private static async Task<long> Create(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    // The outbox delivers after commit, a moment later.
    private static async Task<T> Eventually<T>(Func<Task<T>> read, Func<T, bool> done)
    {
        var value = await read();
        for (var i = 0; i < 50 && !done(value); i++)
        {
            await Task.Delay(200);
            value = await read();
        }
        return value;
    }

    [Fact]
    public async Task Shipping_issues_an_invoice_and_a_payment_lowers_the_customer_balance()
    {
        var manager = await factory.ClientFor("manager@erp.local");
        var rep = await factory.ClientFor("rep@erp.local");
        var clerk = await factory.ClientFor("clerk@erp.local");
        var accountant = await factory.ClientFor("accountant@erp.local");

        var customerId = await Create(manager, "/api/customers",
            new { name = $"Flow {Guid.NewGuid():N}", tin = "111-222-333-000", address = "Pasig", creditLimit = 100_000, paymentTermsDays = 30 });
        var orderId = await Create(rep, "/api/sales-orders",
            new { customerId, lines = new object[] { new { itemId = 1, quantity = 2 }, new { itemId = 5, quantity = 1.5m } } });
        (await rep.PostAsync($"/api/sales-orders/{orderId}/submit", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await clerk.PostAsJsonAsync("/api/shipments", new { salesOrderId = orderId })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await clerk.PostAsJsonAsync("/api/shipments", new { salesOrderId = orderId })).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity, "an order ships once");

        var invoices = await Eventually(
            () => accountant.GetFromJsonAsync<Page<InvoiceRow>>($"/api/invoices?customerId={customerId}")!,
            p => p!.TotalCount == 1);
        var invoice = invoices!.Items.Single();
        invoice.Total.Should().Be(980.75m);

        var pdf = await accountant.GetAsync($"/api/invoices/{invoice.Id}/pdf");
        pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Asia/Manila"));
        (await accountant.PostAsJsonAsync($"/api/invoices/{invoice.Id}/payments",
            new { amount = 980.75m, paidOn = today, method = "BankTransfer", reference = "BDO-123" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var customer = await Eventually(
            () => manager.GetFromJsonAsync<CustomerRow>($"/api/customers/{customerId}")!,
            c => c!.OutstandingBalance == 0);
        customer!.OutstandingBalance.Should().Be(0, "Sales applied the PaymentRecorded event");
        customer.AvailableCredit.Should().Be(100_000);
    }

    [Fact]
    public async Task Invoice_numbers_have_no_gaps()
    {
        await using var conn = new NpgsqlConnection(CustomWebAppFactory.TestConnectionString);
        var numbers = (await conn.QueryAsync<string>("SELECT number FROM invoicing.invoice ORDER BY id")).ToList();
        var counters = numbers.Select(n => int.Parse(n[^6..])).ToList();

        counters.Should().Equal(Enumerable.Range(1, counters.Count));
    }

    [Fact]
    public async Task A_new_catalog_item_reaches_the_price_list_unpriced_and_can_be_priced()
    {
        var clerk = await factory.ClientFor("clerk@erp.local");
        var manager = await factory.ClientFor("manager@erp.local");
        var sku = $"T-{Guid.NewGuid():N}"[..20];

        var itemId = await Create(clerk, "/api/items", new { sku, name = "Test Bracket", unit = "pc", allowsFractions = false });

        var listed = await Eventually(
            () => manager.GetFromJsonAsync<Page<PriceRow>>($"/api/price-list?search={sku}")!,
            p => p!.TotalCount == 1);
        listed!.Items.Single().IsForSale.Should().BeFalse("a new item waits for a price");

        (await manager.PutAsJsonAsync($"/api/price-list/{itemId}", new { unitPrice = 12.5m, categoryId = 1, isForSale = true }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await clerk.PostAsJsonAsync("/api/items", new { sku, name = "Duplicate", unit = "pc", allowsFractions = false }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Receiving_a_purchase_order_puts_stock_on_hand()
    {
        var buyer = await factory.ClientFor("buyer@erp.local");
        var admin = await factory.ClientFor("admin@erp.local");
        var clerk = await factory.ClientFor("clerk@erp.local");
        var before = await OnHand(clerk, "GEN-002");

        var poId = await Create(buyer, "/api/purchase-orders",
            new { supplierId = 1, expectedDate = (DateOnly?)null, notes = "restock", lines = new[] { new { itemId = 2, quantity = 10m, unitCost = 30m } } });
        (await buyer.PostAsync($"/api/purchase-orders/{poId}/approve", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PostAsync($"/api/purchase-orders/{poId}/approve", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var lineId = (await clerk.GetFromJsonAsync<PoDetail>($"/api/purchase-orders/{poId}"))!.Lines.Single().Id;
        (await clerk.PostAsJsonAsync($"/api/purchase-orders/{poId}/receipts", new { lines = new[] { new { lineId, quantity = 4m } } }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await clerk.PostAsJsonAsync($"/api/purchase-orders/{poId}/receipts", new { lines = new[] { new { lineId, quantity = 7m } } }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "only 6 are still to come");

        var po = await clerk.GetFromJsonAsync<PoDetail>($"/api/purchase-orders/{poId}");
        po!.Status.Should().Be("PartiallyReceived");
        po.Lines.Single().Outstanding.Should().Be(6);
        (await OnHand(clerk, "GEN-002")).Should().Be(before + 4);
    }

    [Fact]
    public async Task A_rep_sees_only_their_own_orders()
    {
        var manager = await factory.ClientFor("manager@erp.local");
        var rep = await factory.ClientFor("rep@erp.local");
        var managersOrder = await Create(manager, "/api/sales-orders", new { customerId = 1, lines = new[] { new { itemId = 1, quantity = 1 } } });
        var repsOrder = await Create(rep, "/api/sales-orders", new { customerId = 1, lines = new[] { new { itemId = 1, quantity = 1 } } });

        (await rep.GetAsync($"/api/sales-orders/{managersOrder}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await rep.PostAsync($"/api/sales-orders/{managersOrder}/submit", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await manager.GetAsync($"/api/sales-orders/{repsOrder}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var repsList = await rep.GetFromJsonAsync<Page<OrderRow>>("/api/sales-orders?pageSize=100");
        repsList!.Items.Select(o => o.Id).Should().Contain(repsOrder).And.NotContain(managersOrder);
    }

    private static async Task<decimal> OnHand(HttpClient client, string sku) =>
        (await client.GetFromJsonAsync<Page<ItemRow>>($"/api/items?search={sku}"))!.Items.Single().OnHand;
}
