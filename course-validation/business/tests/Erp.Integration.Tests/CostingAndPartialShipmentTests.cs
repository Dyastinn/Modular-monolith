using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;

namespace Erp.Integration.Tests;

// Cost follows the goods: receipts set the average, shipments carry it onto the invoice; an order can ship in parts.
// Each test makes its own item and customer, so the numbers are its own.
[Collection("Integration")]
public class CostingAndPartialShipmentTests(CustomWebAppFactory factory)
{
    private sealed record Created(long Id);
    private sealed record Page<T>(List<T> Items, int TotalCount);
    private sealed record ItemRow(long Id, string Sku, decimal OnHand, decimal Reserved, decimal? AverageCost, decimal? StockValue);
    private sealed record PoLine(long Id);
    private sealed record PoDetail(List<PoLine> Lines);
    private sealed record OrderLine(long ItemId, decimal ShippedQuantity, decimal Outstanding);
    private sealed record OrderDetail(string Status, List<OrderLine> Lines);
    private sealed record InvoiceRow(long Id, decimal Total);
    private sealed record CustomerRow(decimal OutstandingBalance);
    private sealed record Problem(string Code);

    private static async Task<long> Create(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

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

    private async Task<HttpClient> As(string who) => await factory.ClientFor($"{who}@erp.local");

    private static async Task<ItemRow> Item(HttpClient client, string sku) =>
        (await client.GetFromJsonAsync<Page<ItemRow>>($"/api/items?search={sku}"))!.Items.Single();

    // A new item, on sale at ₱100, with stock received from purchase orders at the given costs.
    private async Task<(long Id, string Sku)> StockedItem(params (decimal Quantity, decimal Cost)[] receipts)
    {
        var clerk = await As("clerk");
        var sku = $"CST-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var id = await Create(clerk, "/api/items", new { sku, name = "Costed widget", unit = "pc", allowsFractions = false });
        var manager = await As("manager");
        await Eventually(() => manager.PutAsJsonAsync($"/api/price-list/{id}", new { unitPrice = 100m, categoryId = 1, isForSale = true }),
            r => r.StatusCode == HttpStatusCode.NoContent);
        foreach (var (quantity, cost) in receipts) await Receive(id, quantity, cost);
        return (id, sku);
    }

    private async Task Receive(long itemId, decimal quantity, decimal cost)
    {
        var buyer = await As("buyer");
        var admin = await As("admin");
        var clerk = await As("clerk");
        var po = await Create(buyer, "/api/purchase-orders",
            new { supplierId = 1, expectedDate = (DateOnly?)null, notes = "", lines = new[] { new { itemId, quantity, unitCost = cost } } });
        await buyer.PostAsync($"/api/purchase-orders/{po}/submit", null);
        await admin.PostAsync($"/api/purchase-orders/{po}/approve", null);
        var line = (await clerk.GetFromJsonAsync<PoDetail>($"/api/purchase-orders/{po}"))!.Lines.Single().Id;
        (await clerk.PostAsJsonAsync($"/api/purchase-orders/{po}/receipts", new { lines = new[] { new { lineId = line, quantity } } }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private async Task<(long OrderId, long CustomerId)> ApprovedOrder(long itemId, int quantity)
    {
        var manager = await As("manager");
        var customer = await Create(manager, "/api/customers",
            new { name = $"Cost test {Guid.NewGuid():N}", creditLimit = 1_000_000m, paymentTermsDays = 30 });
        var rep = await As("rep");
        var order = await Create(rep, "/api/sales-orders", new { customerId = customer, lines = new[] { new { itemId, quantity } } });
        (await rep.PostAsync($"/api/sales-orders/{order}/submit", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return (order, customer);
    }

    [Fact]
    public async Task Receipts_average_the_cost_and_reps_never_see_it()
    {
        var (_, sku) = await StockedItem((10, 30), (30, 34));

        var asClerk = await Item(await As("clerk"), sku);
        asClerk.AverageCost.Should().Be(33m);
        asClerk.StockValue.Should().Be(1_320m);

        var asRep = await Item(await As("rep"), sku);
        (asRep.AverageCost, asRep.StockValue).Should().Be(((decimal?)null, (decimal?)null));
        (await (await As("rep")).GetAsync("/api/stock-valuation")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_order_ships_in_two_parts_and_each_part_gets_its_own_invoice()
    {
        var (itemId, sku) = await StockedItem((20, 40));
        var (order, _) = await ApprovedOrder(itemId, 10);   // ₱1,000
        var clerk = await As("clerk");
        var accountant = await As("accountant");

        await Create(clerk, "/api/shipments", new { salesOrderId = order, lines = new[] { new { itemId, quantity = 4m } } });
        var detail = await clerk.GetFromJsonAsync<OrderDetail>($"/api/sales-orders/{order}");
        detail!.Status.Should().Be("PartiallyShipped");
        detail.Lines.Single().Outstanding.Should().Be(6);
        (await Item(clerk, sku)).Should().Match<ItemRow>(i => i.OnHand == 16 && i.Reserved == 6);

        await Create(clerk, "/api/shipments", new { salesOrderId = order });   // the rest
        (await clerk.GetFromJsonAsync<OrderDetail>($"/api/sales-orders/{order}"))!.Status.Should().Be("Shipped");

        var orderNumber = (await clerk.GetFromJsonAsync<OrderNumberRow>($"/api/sales-orders/{order}"))!.OrderNumber;
        var invoices = await Eventually(
            () => accountant.GetFromJsonAsync<Page<InvoiceRow>>($"/api/invoices?search={orderNumber}")!,
            p => p!.TotalCount == 2);
        invoices!.Items.Select(i => i.Total).Should().BeEquivalentTo([400m, 600m]);
    }

    private sealed record OrderNumberRow(string OrderNumber);

    [Fact]
    public async Task Shipping_more_than_is_left_is_refused_and_no_stock_moves()
    {
        var (itemId, sku) = await StockedItem((20, 40));
        var (order, _) = await ApprovedOrder(itemId, 5);
        var clerk = await As("clerk");

        var response = await clerk.PostAsJsonAsync("/api/shipments", new { salesOrderId = order, lines = new[] { new { itemId, quantity = 6m } } });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<Problem>())!.Code.Should().Be("SALES-0031");
        (await Item(clerk, sku)).Should().Match<ItemRow>(i => i.OnHand == 20 && i.Reserved == 5);
    }

    [Fact]
    public async Task Closing_short_releases_the_rest_and_lowers_the_balance()
    {
        var (itemId, sku) = await StockedItem((20, 40));
        var (order, customer) = await ApprovedOrder(itemId, 10);   // ₱1,000 charged
        var clerk = await As("clerk");
        var rep = await As("rep");
        await Create(clerk, "/api/shipments", new { salesOrderId = order, lines = new[] { new { itemId, quantity = 3m } } });

        (await rep.PostAsync($"/api/sales-orders/{order}/close", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await rep.GetFromJsonAsync<OrderDetail>($"/api/sales-orders/{order}"))!.Status.Should().Be("Closed");
        (await Item(clerk, sku)).Should().Match<ItemRow>(i => i.OnHand == 17 && i.Reserved == 0);
        (await rep.GetFromJsonAsync<CustomerRow>($"/api/customers/{customer}"))!.OutstandingBalance.Should().Be(300m);
    }

    [Fact]
    public async Task Stock_found_can_state_its_cost()
    {
        var (itemId, sku) = await StockedItem((10, 30));
        var clerk = await As("clerk");

        (await clerk.PostAsJsonAsync($"/api/items/{itemId}/adjustments", new { quantity = 10m, reason = "Found", unitCost = 20m }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Item(clerk, sku)).AverageCost.Should().Be(25m);
    }

    private sealed record InvoiceLineRow(long Id, decimal Quantity, decimal CreditedQuantity);
    private sealed record MemoRow(long Id, string Number, decimal Total, decimal AppliedToInvoice, decimal RefundDue);
    private sealed record InvoiceDetail(string Status, decimal BalanceDue, decimal Credited, List<InvoiceLineRow> Lines, List<MemoRow> CreditMemos);

    private async Task<long> ShippedAndInvoiced(long itemId, int quantity)
    {
        var (order, _) = await ApprovedOrder(itemId, quantity);
        var clerk = await As("clerk");
        await Create(clerk, "/api/shipments", new { salesOrderId = order });
        var number = (await clerk.GetFromJsonAsync<OrderNumberRow>($"/api/sales-orders/{order}"))!.OrderNumber;
        var accountant = await As("accountant");
        var invoices = await Eventually(() => accountant.GetFromJsonAsync<Page<InvoiceRow>>($"/api/invoices?search={number}")!, p => p!.TotalCount == 1);
        return invoices!.Items.Single().Id;
    }

    [Fact]
    public async Task Returned_goods_come_back_at_cost_and_the_customer_owes_less()
    {
        var (itemId, sku) = await StockedItem((20, 40));
        var invoiceId = await ShippedAndInvoiced(itemId, 10);   // ₱1,000 billed; 10 left at ₱40 each
        var accountant = await As("accountant");
        var clerk = await As("clerk");
        var line = (await accountant.GetFromJsonAsync<InvoiceDetail>($"/api/invoices/{invoiceId}"))!.Lines.Single();

        await Create(accountant, $"/api/invoices/{invoiceId}/credit-memos",
            new { lines = new[] { new { lineId = line.Id, quantity = 3m } }, reason = "Returned unopened", returnToStock = true });

        var invoice = (await accountant.GetFromJsonAsync<InvoiceDetail>($"/api/invoices/{invoiceId}"))!;
        (invoice.Credited, invoice.BalanceDue).Should().Be((300m, 700m));
        invoice.CreditMemos.Single().Should().Match<MemoRow>(m => m.Number.StartsWith("CM-") && m.AppliedToInvoice == 300 && m.RefundDue == 0);
        (await Item(clerk, sku)).Should().Match<ItemRow>(i => i.OnHand == 13 && i.AverageCost == 40);

        var pdf = await accountant.GetAsync($"/api/credit-memos/{invoice.CreditMemos.Single().Id}/pdf");
        pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task A_credit_on_a_paid_invoice_is_a_refund_and_the_balance_is_not_taken_below_what_was_owed()
    {
        var (itemId, _) = await StockedItem((20, 40));
        var invoiceId = await ShippedAndInvoiced(itemId, 5);   // ₱500
        var accountant = await As("accountant");
        (await accountant.PostAsJsonAsync($"/api/invoices/{invoiceId}/payments",
            new { amount = 500m, taxWithheld = 0m, paidOn = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)), method = "Cash" }))
            .IsSuccessStatusCode.Should().BeTrue();
        var line = (await accountant.GetFromJsonAsync<InvoiceDetail>($"/api/invoices/{invoiceId}"))!.Lines.Single();

        var memoId = await Create(accountant, $"/api/invoices/{invoiceId}/credit-memos",
            new { lines = new[] { new { lineId = line.Id, quantity = 2m } }, reason = "Damaged in delivery", returnToStock = false });

        var memo = (await accountant.GetFromJsonAsync<InvoiceDetail>($"/api/invoices/{invoiceId}"))!.CreditMemos.Single();
        (memo.AppliedToInvoice, memo.RefundDue).Should().Be((0m, 200m));
        (await accountant.PostAsJsonAsync($"/api/credit-memos/{memoId}/refund",
            new { paidOn = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)), method = "BankTransfer", reference = "TRX-1" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Crediting_more_than_was_billed_is_422_and_changes_nothing()
    {
        var (itemId, sku) = await StockedItem((20, 40));
        var invoiceId = await ShippedAndInvoiced(itemId, 2);
        var accountant = await As("accountant");
        var line = (await accountant.GetFromJsonAsync<InvoiceDetail>($"/api/invoices/{invoiceId}"))!.Lines.Single();

        var response = await accountant.PostAsJsonAsync($"/api/invoices/{invoiceId}/credit-memos",
            new { lines = new[] { new { lineId = line.Id, quantity = 3m } }, reason = "Returned", returnToStock = true });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<Problem>())!.Code.Should().Be("INVOICING-0036");
        (await Item(await As("clerk"), sku)).OnHand.Should().Be(18);
        (await (await As("rep")).PostAsJsonAsync($"/api/invoices/{invoiceId}/credit-memos",
            new { lines = new[] { new { lineId = line.Id, quantity = 1m } }, reason = "x", returnToStock = false }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
