using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Erp.Web.UiTests;

// Runs only against a running app with the development seed: set ERP_UI_URL (e.g. http://localhost:5264).
// ERP_UI_SCREENSHOTS, if set, is a folder that receives a screenshot of every step.
// ERP_UI_HEADED, if set, shows the browser window, slowed down.
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
    private readonly List<IBrowserContext> _contexts = [];
    private IPlaywright _playwright = default!;
    private IBrowser _browser = default!;
    private int _shot;

    public async ValueTask InitializeAsync()
    {
        if (BaseUrl.Length == 0) return;
        // A first visit downloads and starts the whole app in the browser; on a busy build machine that can take
        // longer than Expect's 5 seconds. Still a wait for something on screen, just a more patient one.
        SetDefaultExpectTimeout(15_000);
        _playwright = await Playwright.CreateAsync();
        // By default the Edge (or Chrome) already on the machine: nothing to download.
        var channel = Environment.GetEnvironmentVariable("ERP_UI_BROWSER") ?? (OperatingSystem.IsWindows() ? "msedge" : "chrome");
        // ERP_UI_HEADED=1 shows the browser window, slowed down, to watch a test while debugging it.
        var headed = Environment.GetEnvironmentVariable("ERP_UI_HEADED") is not null;
        _browser = await _playwright.Chromium.LaunchAsync(new()
        {
            Channel = channel == "chromium" ? null : channel,
            Headless = !headed,
            SlowMo = headed ? 300 : 0
        });
    }

    // A failed test leaves a trace: every step, with the page as it looked, the network and the console.
    // Open it with `pwsh bin/Debug/net10.0/playwright.ps1 show-trace <file>`, or drop it on trace.playwright.dev.
    public async ValueTask DisposeAsync()
    {
        if (BaseUrl.Length == 0) return;
        var failed = TestContext.Current.TestState?.Result == TestResult.Failed;
        for (var i = 0; i < _contexts.Count; i++)
        {
            var path = Path.Combine("playwright-traces", $"{TestContext.Current.Test?.TestDisplayName}-{i + 1}.zip");
            await _contexts[i].Tracing.StopAsync(failed ? new() { Path = path } : null);
            await _contexts[i].CloseAsync();
        }
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }

    // Each person gets their own browser context: their own cookie, like their own computer.
    private async Task<IPage> NewPage()
    {
        var context = await _browser.NewContextAsync(new() { BaseURL = BaseUrl, ViewportSize = new() { Width = 1366, Height = 820 } });
        await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = true });
        _contexts.Add(context);
        return await context.NewPageAsync();
    }

    private async Task<IPage> SignIn(string email)
    {
        var page = await NewPage();
        await page.GotoAsync("/login");
        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync("Password123!");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await Expect(page.GetByText("Good day,")).ToBeVisibleAsync();
        return page;
    }

    private async Task Shot(IPage page, string name)
    {
        if (Screenshots is null) return;
        Directory.CreateDirectory(Screenshots);
        await page.ScreenshotAsync(new() { Path = Path.Combine(Screenshots, $"{++_shot:00}-{name}.png"), FullPage = true });
    }

    // A MudAutocomplete: type into the box, then click the option in the list that opens under it.
    private static async Task Pick(IPage page, string label, string search, string option)
    {
        await page.GetByRole(AriaRole.Combobox, new() { Name = label }).FillAsync(search);
        await page.GetByRole(AriaRole.Option, new() { Name = option }).ClickAsync();
    }

    // The status chip at the top of an order, purchase order or invoice (a role="status" region).
    private static Task StatusIs(IPage page, string status) =>
        Expect(page.GetByRole(AriaRole.Status).Filter(new() { HasText = status })).ToBeVisibleAsync();

    private static ILocator Breadcrumb(IPage page, string name) =>
        page.GetByRole(AriaRole.Navigation, new() { Name = "Breadcrumb" }).GetByRole(AriaRole.Link, new() { Name = name });

    // A list doesn't refresh by itself, and some records are made in the background (lesson 13's outbox).
    // So reload until the row shows. Each round waits for the list to finish loading (the row, or the list's
    // "nothing matches" text), never for a fixed time: the app restarts on every reload, and that takes a moment.
    private static async Task ReloadUntilVisible(IPage page, string url, ILocator row, string nothingMatches)
    {
        for (var attempt = 1; attempt <= 15; attempt++)
        {
            await page.GotoAsync(url);
            await Expect(row.Or(page.GetByText(nothingMatches))).ToBeVisibleAsync();
            if (await row.IsVisibleAsync()) return;
        }
        await Expect(row).ToBeVisibleAsync();   // fails with Playwright's own message
    }

    [UiFact]
    public async Task Signing_in_greets_you_by_name()
    {
        var page = await SignIn("rep@erp.local");
        await Shot(page, "signed-in");
        await Expect(page).ToHaveURLAsync(BaseUrl + "/");
    }

    [UiFact]
    public async Task A_wrong_password_shows_a_message_and_stays_on_the_sign_in_page()
    {
        var page = await NewPage();
        await page.GotoAsync("/login");
        await page.GetByLabel("Email").FillAsync("rep@erp.local");
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync("not-the-password");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("Wrong email or password.");
        await Expect(page).ToHaveURLAsync(new Regex("/login$"));
    }

    [UiFact]
    public async Task A_page_opened_before_signing_in_asks_for_sign_in()
    {
        var page = await NewPage();
        await page.GotoAsync("/sales-orders");

        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Sign in" })).ToBeVisibleAsync();
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
        await Expect(rep).ToHaveURLAsync(new Regex(@"/sales-orders/\d+$"));
        var orderUrl = new Uri(rep.Url).AbsolutePath;
        await rep.GetByRole(AriaRole.Button, new() { Name = "Submit" }).ClickAsync();
        await StatusIs(rep, "Pending approval");
        await Shot(rep, "rep-submitted");
        var orderNumber = await rep.GetByRole(AriaRole.Heading, new() { Level = 4 }).InnerTextAsync();

        // The manager approves it.
        var manager = await SignIn("manager@erp.local");
        await manager.GotoAsync(orderUrl);
        await manager.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();
        await StatusIs(manager, "Approved");
        await Shot(manager, "manager-approved");

        // The warehouse ships it; Ship asks first, in a dialog.
        var clerk = await SignIn("clerk@erp.local");
        await clerk.GotoAsync(orderUrl);
        await clerk.GetByRole(AriaRole.Button, new() { Name = "Ship" }).ClickAsync();
        await clerk.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Ship" }).ClickAsync();
        await StatusIs(clerk, "Shipped");
        await Shot(clerk, "clerk-shipped");

        // The invoice appears by itself; the accountant records full payment.
        var accountant = await SignIn("accountant@erp.local");
        var invoiceRow = accountant.GetByRole(AriaRole.Row).Filter(new() { HasText = orderNumber });
        await ReloadUntilVisible(accountant, $"/invoices?search={Uri.EscapeDataString(orderNumber)}", invoiceRow, "No invoices match.");
        await Shot(accountant, "accountant-invoices");
        await invoiceRow.ClickAsync();
        await accountant.GetByRole(AriaRole.Button, new() { Name = "Record payment" }).ClickAsync();
        await accountant.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Record", Exact = true }).ClickAsync();
        await StatusIs(accountant, "Paid");
        await Shot(accountant, "accountant-paid");

        await Expect(accountant.GetByRole(AriaRole.Group, new() { Name = "Balance due" })).ToContainTextAsync("₱0.00");
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
            // Loaded: the page has its heading, and no progress bar is still turning.
            var main = admin.GetByRole(AriaRole.Main);
            await Expect(main.GetByRole(AriaRole.Heading).First).ToBeVisibleAsync();
            await Expect(main.GetByRole(AriaRole.Progressbar)).ToHaveCountAsync(0);
            await Shot(admin, path.Trim('/').Replace('/', '-'));

            await Expect(admin.Locator("#blazor-error-ui"), $"{path} crashed the app").ToBeHiddenAsync();
            await Expect(admin.GetByRole(AriaRole.Alert), $"{path} showed an error").ToHaveCountAsync(0);
        }
    }

    [UiFact]
    public async Task A_rep_does_not_see_administration_or_purchasing()
    {
        var rep = await SignIn("rep@erp.local");
        var menu = rep.GetByRole(AriaRole.Navigation);
        await Expect(menu.GetByText("Administration")).ToHaveCountAsync(0);
        await Expect(menu.GetByText("Purchasing")).ToHaveCountAsync(0);

        var admin = await SignIn("admin@erp.local");
        await admin.GotoAsync("/users");
        await Expect(admin.GetByRole(AriaRole.Row).Filter(new() { HasText = "rep@erp.local" })).ToBeVisibleAsync();
        await Shot(admin, "admin-users");
    }

    // A page under a list says where it sits and leads back to the list in one click.
    [UiFact]
    public async Task The_breadcrumb_leads_back_to_the_list()
    {
        var rep = await SignIn("rep@erp.local");
        await rep.GotoAsync("/sales-orders/new");

        await Breadcrumb(rep, "Sales orders").ClickAsync();

        await Expect(rep).ToHaveURLAsync(new Regex(@"/sales-orders$"));
    }

    // A list keeps its search in the address: the breadcrumb returns to it as it was, and the menu starts it fresh.
    // (No data needed: a search that matches nothing is still a search to come back to.)
    [UiFact]
    public async Task A_list_is_remembered_when_you_come_back_to_it()
    {
        var rep = await SignIn("rep@erp.local");
        await rep.GotoAsync("/sales-orders");
        var search = rep.GetByLabel("Order number or customer");
        await search.FillAsync("no such order");
        await Expect(rep).ToHaveURLAsync(new Regex("search=no"));

        await rep.GetByRole(AriaRole.Link, new() { Name = "New order" }).ClickAsync();
        await Breadcrumb(rep, "Sales orders").ClickAsync();

        await Expect(rep).ToHaveURLAsync(new Regex(@"/sales-orders\?search=no"));
        await Expect(search).ToHaveValueAsync("no such order");

        await rep.GetByRole(AriaRole.Navigation).GetByRole(AriaRole.Link, new() { Name = "Sales orders" }).ClickAsync();
        await Expect(rep).ToHaveURLAsync(new Regex(@"/sales-orders$"));
        await Expect(search).ToHaveValueAsync("");
    }

    // A half-filled form asks before it's left; "Stay" keeps the work.
    [UiFact]
    public async Task Leaving_an_unsaved_order_asks_first()
    {
        var rep = await SignIn("rep@erp.local");
        await rep.GotoAsync("/sales-orders/new");
        await Pick(rep, "Customer", "Epsilon", "Epsilon Retail");

        await rep.GetByRole(AriaRole.Link, new() { Name = "Customers" }).ClickAsync();
        var dialog = rep.GetByRole(AriaRole.Dialog, new() { Name = "Leave without saving?" });
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Stay" }).ClickAsync();

        await Expect(dialog).ToBeHiddenAsync();
        await Expect(rep).ToHaveURLAsync(new Regex("/sales-orders/new$"));
        await Expect(rep.GetByRole(AriaRole.Combobox, new() { Name = "Customer" })).ToHaveValueAsync("Epsilon Retail");
    }

    // The eye button shows what was typed, so a typo can be seen before signing in.
    [UiFact]
    public async Task The_password_can_be_shown_before_signing_in()
    {
        var page = await NewPage();
        await page.GotoAsync("/login");
        var password = page.GetByLabel("Password", new() { Exact = true });
        await password.FillAsync("Secret-1234");

        await page.GetByRole(AriaRole.Button, new() { Name = "Show password" }).ClickAsync();

        await Expect(password).ToHaveAttributeAsync("type", "text");
    }

    // The account button opens its menu, and signing out from it lands on the sign-in page.
    [UiFact]
    public async Task The_account_menu_opens_and_signs_out()
    {
        var rep = await SignIn("rep@erp.local");
        await rep.GetByRole(AriaRole.Button, new() { Name = "Rita Reyes" }).ClickAsync();
        await rep.GetByRole(AriaRole.Menuitem, new() { Name = "Sign out", Exact = true }).ClickAsync();

        await Expect(rep.GetByRole(AriaRole.Button, new() { Name = "Sign in" })).ToBeVisibleAsync();
    }

    // The choice of dark mode is remembered in this browser: a reload keeps it.
    [UiFact]
    public async Task Dark_mode_is_remembered_after_a_reload()
    {
        var rep = await SignIn("rep@erp.local");
        await rep.GetByRole(AriaRole.Button, new() { Name = "Dark mode" }).ClickAsync();
        await Expect(rep.GetByRole(AriaRole.Button, new() { Name = "Light mode" })).ToBeVisibleAsync();

        await rep.ReloadAsync();

        await Expect(rep.GetByRole(AriaRole.Button, new() { Name = "Light mode" })).ToBeVisibleAsync();
    }

    // A refusal names its code, and one click explains it: the way a compiler error's CS number does.
    [UiFact]
    public async Task A_refusal_shows_its_code_and_one_click_explains_it()
    {
        var admin = await SignIn("admin@erp.local");
        await admin.GotoAsync("/users");
        var own = admin.GetByRole(AriaRole.Row).Filter(new() { HasText = "admin@erp.local" });
        await own.GetByRole(AriaRole.Button, new() { Name = "Actions" }).ClickAsync();
        await admin.GetByRole(AriaRole.Menuitem, new() { Name = "Disable" }).ClickAsync();

        await Expect(admin.GetByRole(AriaRole.Alert)).ToContainTextAsync("(IDENTITY-0003)");
        await Shot(admin, "error-with-code");
        await admin.GetByRole(AriaRole.Button, new() { Name = "What does this mean?" }).ClickAsync();

        await Expect(admin.GetByRole(AriaRole.Heading, new() { Name = "You can't disable your own account" })).ToBeVisibleAsync();
        await Expect(admin.GetByText("What to do", new() { Exact = true })).ToBeVisibleAsync();
        await Shot(admin, "error-explained");
    }

    // The link in an error message must open even when the session has ended.
    [UiFact]
    public async Task The_error_catalog_opens_without_signing_in()
    {
        var page = await NewPage();

        await page.GotoAsync("/errors/SALES-0003");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "The order would exceed the credit limit" })).ToBeVisibleAsync();
        await Expect(page, "the catalog doesn't send anyone to sign in").ToHaveURLAsync(new Regex("/errors/SALES-0003$"));
    }
}
