using System.Runtime.CompilerServices;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Yasda.Web.UiTests;

// Runs only against a running app with the development seed: set YASDA_UI_URL (e.g. http://localhost:5264).
// YASDA_UI_SCREENSHOTS, if set, is a folder that receives a screenshot of every step.
// YASDA_UI_HEADED, if set, shows the browser window, slowed down.
// YASDA_UI_BROWSER picks the browser: msedge (Windows default), chrome (elsewhere), or chromium for the one
// `playwright install chromium` downloads, on a machine with neither.
public sealed class UiFactAttribute : FactAttribute
{
    // xUnit v3 records where each test is written; an attribute built on Fact passes that location on.
    public UiFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("YASDA_UI_URL")))
            Skip = "Set YASDA_UI_URL to a running YASDA to run browser tests.";
    }
}

// What every browser test needs: a browser, one context per person, a sign-in, and a trace when a test fails.
public abstract class UiTest : IAsyncLifetime
{
    protected const string Password = "Password123!";
    protected static readonly string BaseUrl = Environment.GetEnvironmentVariable("YASDA_UI_URL")?.TrimEnd('/') ?? "";
    private static readonly string? Screenshots = Environment.GetEnvironmentVariable("YASDA_UI_SCREENSHOTS");
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
        var channel = Environment.GetEnvironmentVariable("YASDA_UI_BROWSER") ?? (OperatingSystem.IsWindows() ? "msedge" : "chrome");
        var headed = Environment.GetEnvironmentVariable("YASDA_UI_HEADED") is not null;
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
        GC.SuppressFinalize(this);
    }

    // Each person gets their own browser context: their own cookie, like their own computer. A phone-sized viewport
    // is the narrow case DESIGN.md's single column must handle.
    protected async Task<IPage> NewPage(int width = 1366)
    {
        var context = await _browser.NewContextAsync(new() { BaseURL = BaseUrl, ViewportSize = new() { Width = width, Height = 820 } });
        await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = true });
        _contexts.Add(context);
        return await context.NewPageAsync();
    }

    protected static async Task FillSignIn(IPage page, string email, string password)
    {
        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
    }

    // Signed in once Home shows with its own entry in the navigation marked as the current page. Each role's Home has
    // its own heading, so the navigation is what every role shares.
    protected async Task<IPage> SignIn(string email, int width = 1366)
    {
        var page = await NewPage(width);
        await page.GotoAsync("/login");
        await FillSignIn(page, email, Password);
        await Expect(MainNav(page).Locator("[aria-current=page]")).ToHaveTextAsync("Home");
        return page;
    }

    protected async Task Shot(IPage page, string name)
    {
        if (Screenshots is null) return;
        Directory.CreateDirectory(Screenshots);
        await page.ScreenshotAsync(new() { Path = Path.Combine(Screenshots, $"{GetType().Name}-{++_shot:00}-{name}.png"), FullPage = true });
    }

    protected static ILocator MainNav(IPage page) => page.GetByRole(AriaRole.Navigation, new() { Name = "Main" });

    // The status line with this message. Found by its text: a page may show its own "Loading…" status at the same time.
    protected static ILocator StatusSaying(IPage page, string text)
    {
        return page.GetByRole(AriaRole.Status).Filter(new() { HasText = text });
    }
}
