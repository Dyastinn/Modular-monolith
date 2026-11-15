using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;

namespace Erp.Integration.Tests;

// Going live from spreadsheets: every import is checked first, then saved all at once or not at all.
[Collection("Integration")]
public class ImportTests(CustomWebAppFactory factory)
{
    private sealed record ImportError(int Row, string Message, string? Code);
    private sealed record ImportResult(int Rows, int Imported, List<ImportError> Errors);
    private sealed record Page<T>(List<T> Items, int TotalCount);
    private sealed record CustomerRow(long Id, string Name, decimal OutstandingBalance);
    private sealed record ItemRow(long Id, string Sku, decimal OnHand, decimal? AverageCost);
    private sealed record PriceRow(string Sku, decimal UnitPrice, bool IsForSale);
    private sealed record InvoiceRow(long Id, string Number, decimal Total, bool IsOpeningBalance);

    private async Task<HttpClient> As(string who) => await factory.ClientFor($"{who}@erp.local");

    private static async Task<ImportResult> Import(HttpClient client, string url, object rows, bool commit)
    {
        var response = await client.PostAsJsonAsync(url, new { rows, commit });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ImportResult>())!;
    }

    private static async Task<T> Eventually<T>(Func<Task<T>> read, Func<T, bool> done)
    {
        var value = await read();
        for (var i = 0; i < 50 && !done(value); i++) { await Task.Delay(200); value = await read(); }
        return value;
    }

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..(prefix.Length + 9)];

    [Fact]
    public async Task A_check_saves_nothing_and_one_bad_row_stops_the_whole_file()
    {
        var admin = await As("admin");
        var good = Unique("Import good");
        var rows = new[]
        {
            new { name = good, creditLimit = 10_000m, paymentTermsDays = 30 },
            new { name = "Acme Corp", creditLimit = 10_000m, paymentTermsDays = 30 },   // already a customer
        };

        var check = await Import(admin, "/api/customers/import", rows, commit: false);
        check.Errors.Should().ContainSingle().Which.Should().Match<ImportError>(e => e.Row == 2 && e.Code == "SALES-0033");

        var commit = await Import(admin, "/api/customers/import", rows, commit: true);
        commit.Imported.Should().Be(0);
        (await admin.GetFromJsonAsync<Page<CustomerRow>>($"/api/customers?search={Uri.EscapeDataString(good)}"))!.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Customers_import_once_and_a_second_run_is_refused()
    {
        var admin = await As("admin");
        var name = Unique("Import once");
        var rows = new[] { new { name, creditLimit = 25_000m, paymentTermsDays = 15 } };

        (await Import(admin, "/api/customers/import", rows, commit: true)).Imported.Should().Be(1);

        (await Import(admin, "/api/customers/import", rows, commit: true)).Errors.Should().ContainSingle();
    }

    [Fact]
    public async Task Items_arrive_with_opening_stock_at_cost_then_take_their_prices()
    {
        var admin = await As("admin");
        var sku = $"IMP-{Guid.NewGuid():N}"[..16].ToUpperInvariant();
        var items = new[] { new { sku, name = "Imported bolt", unit = "pc", allowsFractions = false, quantity = 120m, unitCost = 7.25m } };

        (await Import(admin, "/api/items/import", items, commit: true)).Imported.Should().Be(1);
        var item = (await admin.GetFromJsonAsync<Page<ItemRow>>($"/api/items?search={sku}"))!.Items.Single();
        (item.OnHand, item.AverageCost).Should().Be((120m, (decimal?)7.25m));

        var prices = new[] { new { sku, unitPrice = 12m, category = "General", isForSale = true } };
        var priced = await Eventually(() => Import(admin, "/api/price-list/import", prices, commit: true), r => r.Imported == 1);
        priced.Errors.Should().BeEmpty();
        var row = (await admin.GetFromJsonAsync<Page<PriceRow>>($"/api/price-list?search={sku}"))!.Items.Single();
        (row.UnitPrice, row.IsForSale).Should().Be((12m, true));
    }

    [Fact]
    public async Task An_opening_balance_counts_against_credit_shows_in_invoices_and_is_never_printed()
    {
        var admin = await As("admin");
        var accountant = await As("accountant");
        var name = Unique("Old debtor");
        await Import(admin, "/api/customers/import", new[] { new { name, creditLimit = 100_000m, paymentTermsDays = 30 } }, commit: true);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        var rows = new[] { new { customer = name, reference = "OLD-0042", invoiceDate = today.AddDays(-40), amount = 18_500m } };

        (await Import(accountant, "/api/opening-balances/import", rows, commit: true)).Imported.Should().Be(1);

        var customer = await Eventually(
            async () => (await admin.GetFromJsonAsync<Page<CustomerRow>>($"/api/customers?search={Uri.EscapeDataString(name)}"))!.Items.Single(),
            c => c.OutstandingBalance == 18_500m);
        customer.OutstandingBalance.Should().Be(18_500m);

        var invoice = (await accountant.GetFromJsonAsync<Page<InvoiceRow>>($"/api/invoices?search=OLD-0042&customerId={customer.Id}"))!.Items.Single();
        invoice.Should().Match<InvoiceRow>(i => i.IsOpeningBalance && i.Number.StartsWith("OB-") && i.Total == 18_500m);
        (await accountant.GetAsync($"/api/invoices/{invoice.Id}/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await Import(accountant, "/api/opening-balances/import", rows, commit: false)).Errors
            .Should().ContainSingle().Which.Code.Should().Be("INVOICING-0030");

        (await accountant.PostAsJsonAsync($"/api/invoices/{invoice.Id}/void", new { reason = "Was paid before go-live" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Eventually(
            async () => (await admin.GetFromJsonAsync<Page<CustomerRow>>($"/api/customers?search={Uri.EscapeDataString(name)}"))!.Items.Single(),
            c => c.OutstandingBalance == 0)).OutstandingBalance.Should().Be(0);
    }

    [Fact]
    public async Task An_unknown_customer_or_a_future_date_is_reported_by_row()
    {
        var accountant = await As("accountant");
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)).AddDays(1);
        var rows = new object[]
        {
            new { customer = "Nobody Trading", reference = "X-1", invoiceDate = tomorrow.AddDays(-10), amount = 100m },
            new { customer = "Acme Corp", reference = "X-2", invoiceDate = tomorrow, amount = 100m },
        };

        var result = await Import(accountant, "/api/opening-balances/import", rows, commit: false);

        result.Errors.Select(e => (e.Row, e.Code)).Should().BeEquivalentTo([(1, "INVOICING-0029"), (2, "INVOICING-0025")]);
    }

    [Theory]
    [InlineData("/api/customers/import")]
    [InlineData("/api/items/import")]
    [InlineData("/api/price-list/import")]
    [InlineData("/api/opening-balances/import")]
    public async Task A_sales_rep_imports_nothing(string url) =>
        (await (await As("rep")).PostAsJsonAsync(url, new { rows = Array.Empty<object>() })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
}
