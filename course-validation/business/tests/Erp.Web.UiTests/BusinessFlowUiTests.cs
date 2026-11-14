using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.Playwright;

namespace Erp.Web.UiTests;

// Runs only against a running app with the development seed: set ERP_UI_URL (e.g. http://localhost:5264).
// ERP_UI_SCREENSHOTS, if set, is a folder that receives a screenshot of every step.
// ERP_UI_BROWSER picks the browser: msedge (Windows default), chrome (elsewhere), or chromium for the one
// `playwright install chromium` downloads, on a machine with neither.
public sealed class UiFactAttribute : FactAttribute
{
    // xUnit v3 records where each test is written; an attribute built on Fact passes that location on.
    public UiFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ERP_UI_URL")))
            Skip = "Set ERP_UI_URL to a running ERP to run browser tests.";
    }
}

public sealed class BusinessFlowUiTests : IAsyncLifetime
{
    private static readonly string BaseUrl = Environment.GetEnvironmentVariable("ERP_UI_URL")?.TrimEnd('/') ?? "";
    private static readonly string? Screenshots = Environment.GetEnvironmentVariable("ERP_UI_SCREENSHOTS");
    private IPlaywright _playwright = default!;
    private IBrowser _browser = default!;
    private int _shot;

    public async ValueTask InitializeAsync()
    {
        if (BaseUrl.Length == 0) return;
        _playwright = await Playwright.CreateAsync();
        // By default the Edge (or Chrome) already on the machine: nothing to download.
        var channel = Environment.GetEnvironmentVariable("ERP_UI_BROWSER") ?? (OperatingSystem.IsWindows() ? "msedge" : "chrome");
        _browser = await _playwright.Chromium.LaunchAsync(new() { Channel = channel == "chromium" ? null : channel });
    }

    public async ValueTask DisposeAsync()
    {
        if (BaseUrl.Length == 0) return;
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }

    // Each person gets their own browser context: their own cookie, like their own computer.
    private async Task<IPage> SignIn(string email)
    {
        var context = await _browser.NewContextAsync(new() { BaseURL = BaseUrl, ViewportSize = new() { Width = 1366, Height = 820 } });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/login");
        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Password").FillAsync("Password123!");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await page.GetByText("Good day,").WaitForAsync();
        return page;
    }

    private async Task Shot(IPage page, string name)
    {
        if (Screenshots is null) return;
        Directory.CreateDirectory(Screenshots);
        await page.ScreenshotAsync(new() { Path = Path.Combine(Screenshots, $"{++_shot:00}-{name}.png"), FullPage = true });
    }

    private static async Task Pick(IPage page, string label, string search, string option)
    {
        await page.GetByLabel(label).First.FillAsync(search);
        await page.GetByText(option).First.ClickAsync();
    }

    [UiFact]
    public async Task Signing_in_greets_you_by_name()
    {
        var page = await SignIn("rep@erp.local");
        await Shot(page, "signed-in");
        new Uri(page.Url).AbsolutePath.Should().Be("/");
    }

    [UiFact]
    public async Task A_wrong_password_shows_a_message_and_stays_on_the_sign_in_page()
    {
        var context = await _browser.NewContextAsync(new() { BaseURL = BaseUrl });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/login");
        await page.GetByLabel("Email").FillAsync("rep@erp.local");
        await page.GetByLabel("Password").FillAsync("not-the-password");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();

        await page.GetByText("Wrong email or password.").WaitForAsync();
        new Uri(page.Url).AbsolutePath.Should().Be("/login");
    }

    [UiFact]
    public async Task A_page_opened_before_signing_in_asks_for_sign_in()
    {
        var context = await _browser.NewContextAsync(new() { BaseURL = BaseUrl });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/sales-orders");

        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).WaitForAsync();
    }

    [UiFact]
    public async Task From_order_to_paid_invoice_through_the_screens()
    {
        // A rep writes an order over ₱50,000 for Epsilon Retail and submits it.
        var rep = await SignIn("rep@erp.local");
        await Shot(rep, "rep-home");
        await rep.GotoAsync("/sales-orders/new");
        await Pick(rep, "Customer", "Epsilon", "Epsilon Retail");
        await Pick(rep, "Item", "Rack", "GEN-003 · Server Rack");
        await rep.GetByRole(AriaRole.Button, new() { Name = "Save draft" }).ClickAsync();
        await rep.WaitForURLAsync(new Regex(@"/sales-orders/\d+$"));
        var orderUrl = new Uri(rep.Url).AbsolutePath;
        await rep.GetByRole(AriaRole.Button, new() { Name = "Submit" }).ClickAsync();
        await rep.Locator(".mud-chip").Filter(new() { HasText = "Pending approval" }).WaitForAsync();
        await Shot(rep, "rep-submitted");
        var orderNumber = await rep.Locator("h4").First.InnerTextAsync();

        // The manager approves it.
        var manager = await SignIn("manager@erp.local");
        await manager.GotoAsync(orderUrl);
        await manager.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();
        await manager.Locator(".mud-chip").Filter(new() { HasText = "Approved" }).WaitForAsync();
        await Shot(manager, "manager-approved");

        // The warehouse ships it.
        var clerk = await SignIn("clerk@erp.local");
        await clerk.GotoAsync(orderUrl);
        await clerk.GetByRole(AriaRole.Button, new() { Name = "Ship" }).ClickAsync();
        await clerk.Locator(".mud-chip").Filter(new() { HasText = "Shipped" }).WaitForAsync();
        await Shot(clerk, "clerk-shipped");

        // The invoice appears by itself; the accountant records full payment.
        var accountant = await SignIn("accountant@erp.local");
        var invoiceRow = accountant.GetByRole(AriaRole.Row).Filter(new() { HasText = orderNumber });
        for (var attempt = 0; attempt < 15 && await invoiceRow.CountAsync() == 0; attempt++)
        {
            await accountant.GotoAsync($"/invoices?search={Uri.EscapeDataString(orderNumber)}");
            await accountant.WaitForTimeoutAsync(1000);
        }
        await Shot(accountant, "accountant-invoices");
        await invoiceRow.First.ClickAsync();
        await accountant.GetByRole(AriaRole.Button, new() { Name = "Record payment" }).ClickAsync();
        await accountant.GetByRole(AriaRole.Button, new() { Name = "Record", Exact = true }).ClickAsync();
        await accountant.Locator(".mud-chip").Filter(new() { HasText = "Paid" }).WaitForAsync();
        await Shot(accountant, "accountant-paid");

        (await accountant.GetByRole(AriaRole.Row).Filter(new() { HasText = "Balance due" }).InnerTextAsync()).Should().Contain("₱0.00");
    }

    [UiFact]
    public async Task Every_page_opens_without_an_error()
    {
        var admin = await SignIn("admin@erp.local");
        string[] pages =
        [
            "/sales-orders", "/sales-orders/new", "/customers", "/price-list", "/items", "/shipments",
            "/suppliers", "/purchase-orders", "/purchase-orders/new", "/invoices", "/receivables-aging", "/withholding-tax", "/company",
            "/users", "/sign-in-log", "/change-password"
        ];

        foreach (var path in pages)
        {
            await admin.GotoAsync(path);
            await admin.Locator("h4, h5").First.WaitForAsync();
            await admin.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await Shot(admin, path.Trim('/').Replace('/', '-'));

            (await admin.Locator("#blazor-error-ui").IsVisibleAsync()).Should().BeFalse($"{path} crashed the app");
            (await admin.Locator(".mud-alert-filled-error, .mud-snackbar.mud-alert-filled-error").CountAsync())
                .Should().Be(0, $"{path} showed an error");
        }
    }

    [UiFact]
    public async Task A_rep_does_not_see_administration_or_purchasing()
    {
        var rep = await SignIn("rep@erp.local");
        (await rep.GetByText("Administration").CountAsync()).Should().Be(0);
        (await rep.GetByText("Purchasing").CountAsync()).Should().Be(0);

        var admin = await SignIn("admin@erp.local");
        await admin.GotoAsync("/users");
        await admin.GetByText("rep@erp.local").WaitForAsync();
        await Shot(admin, "admin-users");
    }

    // The account button opens its menu, and signing out from it lands on the sign-in page.
    [UiFact]
    public async Task The_account_menu_opens_and_signs_out()
    {
        var rep = await SignIn("rep@erp.local");
        await rep.GetByRole(AriaRole.Button, new() { Name = "Rita Reyes" }).ClickAsync();
        await rep.GetByText("Sales rep").WaitForAsync();
        await rep.GetByText("Sign out", new() { Exact = true }).ClickAsync();

        await rep.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).WaitForAsync();
    }

    // The choice of dark mode is remembered in this browser: a reload keeps it.
    [UiFact]
    public async Task Dark_mode_is_remembered_after_a_reload()
    {
        var rep = await SignIn("rep@erp.local");
        await rep.GetByRole(AriaRole.Button, new() { Name = "Dark mode" }).ClickAsync();
        await rep.GetByRole(AriaRole.Button, new() { Name = "Light mode" }).WaitForAsync();

        await rep.ReloadAsync();

        await rep.GetByRole(AriaRole.Button, new() { Name = "Light mode" }).WaitForAsync();
    }

    // A refusal names its code, and one click explains it: the way a compiler error's CS number does.
    [UiFact]
    public async Task A_refusal_shows_its_code_and_one_click_explains_it()
    {
        var admin = await SignIn("admin@erp.local");
        await admin.GotoAsync("/users");
        var own = admin.GetByRole(AriaRole.Row).Filter(new() { HasText = "admin@erp.local" });
        await own.GetByLabel("Actions").ClickAsync();
        await admin.GetByText("Disable").ClickAsync();

        await admin.GetByText("(IDENTITY-0003)").WaitForAsync();
        await Shot(admin, "error-with-code");
        await admin.GetByRole(AriaRole.Button, new() { Name = "What does this mean?" }).ClickAsync();

        await admin.GetByText("You can't disable your own account").First.WaitForAsync();
        await admin.GetByText("What to do:").WaitForAsync();
        await Shot(admin, "error-explained");
    }

    // The link in an error message must open even when the session has ended.
    [UiFact]
    public async Task The_error_catalog_opens_without_signing_in()
    {
        var context = await _browser.NewContextAsync(new() { BaseURL = BaseUrl });
        var page = await context.NewPageAsync();

        await page.GotoAsync("/errors/SALES-0003");

        await page.GetByText("The order would exceed the credit limit").First.WaitForAsync();
        page.Url.Should().EndWith("/errors/SALES-0003", "the catalog doesn't send anyone to sign in");
    }
}
