using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Yasda.Web.UiTests;

public sealed class UserAdminUiTests : UiTest
{
    private static ILocator Field(IPage page, string label) => page.GetByLabel(label, new() { Exact = true });

    [UiFact]
    public async Task An_admin_creates_a_user_then_disables_them_after_confirming()
    {
        var page = await SignIn("admin@yasda.local");
        var email = $"new-{Guid.NewGuid():N}@yasda.local";

        await MainNav(page).GetByRole(AriaRole.Link, new() { Name = "Users" }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "New user" }).ClickAsync();
        await Field(page, "Email").FillAsync(email);
        await Field(page, "Full name").FillAsync("Nina Lopez");
        await Field(page, "Role").SelectOptionAsync("Agent");
        await Field(page, "Temporary password").FillAsync("a temporary password");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create user" }).ClickAsync();

        await Expect(StatusSaying(page, "User created.")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Nina Lopez" })).ToBeVisibleAsync();
        await Expect(page.GetByText("Status: Temporary password")).ToBeVisibleAsync();
        await Shot(page, "user-created");

        await page.GetByRole(AriaRole.Button, new() { Name = "Disable" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Disable Nina Lopez?");
        await page.GetByRole(AriaRole.Button, new() { Name = "Disable" }).ClickAsync();

        await Expect(page.GetByText("Status: Disabled")).ToBeVisibleAsync();
        await Expect(StatusSaying(page, "Disabled.")).ToBeVisibleAsync();
    }

    [UiFact]
    public async Task Not_yet_cancels_the_disable()
    {
        var page = await SignIn("admin@yasda.local");
        await page.GotoAsync("/users?search=Rita");
        await page.GetByRole(AriaRole.Link, new() { Name = "Rita Cruz" }).ClickAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Disable" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Not yet" }).ClickAsync();

        await Expect(page.GetByText("This account can sign in.")).ToBeVisibleAsync();
    }

    // The list's search and sort are links and live in the address, so Back and a pasted link show the same list.
    [UiFact]
    public async Task The_user_list_searches_and_sorts_through_its_address()
    {
        var page = await SignIn("admin@yasda.local");
        await page.GotoAsync("/users");

        await page.GetByRole(AriaRole.Link, new() { Name = "Email" }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("sort=email$"));
        await Expect(page.GetByRole(AriaRole.Columnheader, new() { Name = "Email ▲" })).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Link, new() { Name = "Email" }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("sort=email&desc=true$"));
        await Expect(page.GetByRole(AriaRole.Columnheader, new() { Name = "Email ▼" })).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Searchbox, new() { Name = "Name or email" }).FillAsync("Kira");
        await page.GetByRole(AriaRole.Button, new() { Name = "Search" }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("search=Kira"));
        await Expect(page.GetByRole(AriaRole.Row)).ToHaveCountAsync(2);   // the header and Kira
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Kira Santos" })).ToBeVisibleAsync();
    }

    // An error names its code, and the code links to what it means and how to fix it.
    [UiFact]
    public async Task A_refused_change_shows_its_code_and_the_code_explains_itself()
    {
        var page = await SignIn("admin@yasda.local");
        await page.GotoAsync("/users?search=admin%40yasda.local");
        await page.GetByRole(AriaRole.Link, new() { Name = "Ada Mendoza" }).ClickAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Disable" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Disable" }).ClickAsync();

        var error = page.GetByRole(AriaRole.Alert).Filter(new() { HasText = "Error:" });
        await Expect(error).ToContainTextAsync("You can't disable your own account");
        await error.GetByRole(AriaRole.Link, new() { Name = "What does IDENTITY-0003 mean?" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "IDENTITY-0003: You can't disable your own account" })).ToBeVisibleAsync();
    }

    [UiFact]
    public async Task Someone_who_is_not_an_admin_neither_sees_nor_opens_the_user_pages()
    {
        var page = await SignIn("agent@yasda.local");

        await Expect(MainNav(page).GetByRole(AriaRole.Link, new() { Name = "Users" })).ToHaveCountAsync(0);
        await page.GotoAsync("/users");
        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("You don't have access to this page.");
    }

    [UiFact]
    public async Task Leaving_a_half_filled_new_user_asks_first()
    {
        var page = await SignIn("admin@yasda.local");
        await page.GotoAsync("/users/new");
        await Field(page, "Full name").FillAsync("Half Done");

        page.Dialog += async (_, dialog) => await dialog.DismissAsync();   // "Stay"
        await MainNav(page).GetByRole(AriaRole.Link, new() { Name = "Home" }).ClickAsync();

        await Expect(page).ToHaveURLAsync(BaseUrl + "/users/new");
        await Expect(Field(page, "Full name")).ToHaveValueAsync("Half Done");
    }
}
