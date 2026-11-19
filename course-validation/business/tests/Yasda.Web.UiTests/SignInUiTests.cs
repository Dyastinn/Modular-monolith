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
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Good day, Alan Reyes" })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Navigation, new() { Name = "Account" })).ToContainTextAsync("Alan Reyes (Agent)");
    }

    [UiFact]
    public async Task A_wrong_password_shows_a_message_and_stays_on_the_sign_in_page()
    {
        var page = await NewPage();
        await page.GotoAsync("/login");
        await FillSignIn(page, "agent@yasda.local", "not-the-password");

        await Expect(page.GetByRole(AriaRole.Alert)).ToHaveTextAsync("Wrong email or password.");
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

    // DESIGN.md: one column at any width. A phone shows the same page with nothing pushed off the side.
    [UiFact]
    public async Task The_page_fits_a_phone_without_sideways_scrolling()
    {
        var page = await NewPage(width: 390);
        await page.GotoAsync("/login");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Sign in" })).ToBeVisibleAsync();

        var overflow = await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > document.documentElement.clientWidth");

        Assert.False(overflow, "the page is wider than the phone");
    }
}

