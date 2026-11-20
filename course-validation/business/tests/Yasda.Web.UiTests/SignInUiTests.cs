using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Yasda.Web.UiTests;

public sealed class SignInUiTests : UiTest
{
    [UiFact]
    public async Task Signing_in_greets_you_by_name()
    {
        var page = await SignIn("agent@yasda.local");
        await Shot(page, "signed-in");

        await Expect(page).ToHaveURLAsync(BaseUrl + "/");
        await Expect(page.GetByRole(AriaRole.Heading, new() { NameRegex = new Regex("^Good (morning|afternoon|evening), Alan Reyes$") }))
            .ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Needs you now" })).ToBeVisibleAsync();
    }

    // DESIGN.md §4.1: a requester's Home asks how it can help, with the two main tasks as the first things on it.
    [UiFact]
    public async Task A_requester_lands_on_how_can_we_help()
    {
        var page = await SignIn("requester@yasda.local");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "How can we help?" })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Submit a ticket" })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Needs you now" })).ToHaveCountAsync(0);
    }

    // DESIGN.md §3: the page you're on is bold text in the navigation, not a link to itself.
    [UiFact]
    public async Task The_current_page_is_marked_and_not_a_link()
    {
        var page = await SignIn("agent@yasda.local");
        await MainNav(page).GetByRole(AriaRole.Link, new() { Name = "Tickets" }).ClickAsync();

        await Expect(MainNav(page).Locator("[aria-current=page]")).ToHaveTextAsync("Tickets");
        await Expect(MainNav(page).GetByRole(AriaRole.Link, new() { Name = "Tickets", Exact = true })).ToHaveCountAsync(0);
        await Expect(MainNav(page).GetByRole(AriaRole.Link, new() { Name = "Home" })).ToBeVisibleAsync();
    }

    [UiFact]
    public async Task A_wrong_password_shows_a_message_and_stays_on_the_sign_in_page()
    {
        var page = await NewPage();
        await page.GotoAsync("/login");
        await FillSignIn(page, "agent@yasda.local", "not-the-password");

        await Expect(page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("Error: Wrong email or password.");
        await Expect(page).ToHaveURLAsync(BaseUrl + "/login");
    }

    [UiFact]
    public async Task The_password_can_be_shown_to_check_it()
    {
        var page = await NewPage();
        await page.GotoAsync("/login");
        var password = page.GetByLabel("Password", new() { Exact = true });
        await password.FillAsync("typed-this");

        await Expect(password).ToHaveAttributeAsync("type", "password");
        await page.GetByRole(AriaRole.Button, new() { Name = "Show password" }).ClickAsync();
        await Expect(password).ToHaveAttributeAsync("type", "text");
    }

    // A deep link while signed out goes through sign-in and lands where it pointed.
    [UiFact]
    public async Task A_link_opened_while_signed_out_returns_there_after_sign_in()
    {
        var page = await NewPage();
        await page.GotoAsync("/sign-in-log");
        await Expect(page).ToHaveURLAsync(BaseUrl + "/login?returnUrl=sign-in-log");

        await FillSignIn(page, "admin@yasda.local", Password);

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Sign-in log" })).ToBeVisibleAsync();
    }

    [UiFact]
    public async Task Signing_out_returns_to_the_sign_in_page()
    {
        var page = await SignIn("agent@yasda.local");

        await page.GetByRole(AriaRole.Button, new() { Name = "Sign out", Exact = true }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Sign in" })).ToBeVisibleAsync();
        await page.GotoAsync("/");
        await Expect(page).ToHaveURLAsync(BaseUrl + "/login");
    }

    // DESIGN.md §3: works at 320px with no sideways page scroll. A phone shows the same page with nothing pushed off the side.
    [UiFact]
    public async Task The_page_fits_a_phone_without_sideways_scrolling()
    {
        var page = await NewPage(width: 320);
        await page.GotoAsync("/login");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Sign in" })).ToBeVisibleAsync();

        Assert.False(await WiderThanTheScreen(page), "the page is wider than the phone");
    }

    // The hard case: the full navigation wrapping under the logo, and a wide table, which scrolls in its own box.
    [UiFact]
    public async Task A_list_fits_a_phone_without_sideways_scrolling()
    {
        var page = await SignIn("admin@yasda.local", width: 320);
        await page.GotoAsync("/tickets");
        await Expect(page.GetByRole(AriaRole.Region, new() { Name = "Tickets" })).ToBeVisibleAsync();

        Assert.False(await WiderThanTheScreen(page), "the page is wider than the phone");
    }

    private static Task<bool> WiderThanTheScreen(IPage page) =>
        page.EvaluateAsync<bool>("document.documentElement.scrollWidth > document.documentElement.clientWidth");
}

