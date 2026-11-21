using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Yasda.Web.UiTests;

public sealed class AutomationUiTests : UiTest
{
    [UiFact]
    public async Task An_admin_switches_a_rule_on_changes_its_days_and_switches_it_off_again()
    {
        var page = await SignIn("admin@yasda.local");
        await MainNav(page).GetByRole(AriaRole.Link, new() { Name = "Automatic rules" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Automatic rules" })).ToBeVisibleAsync();
        await Expect(page.GetByLabel("Close a ticket that has been resolved for a number of days")).Not.ToBeCheckedAsync();
        await Expect(page.GetByLabel("Days after it was resolved")).ToHaveCountAsync(0);

        await page.GetByLabel("Close a ticket that has been resolved for a number of days").CheckAsync();
        await page.GetByLabel("Days after it was resolved").FillAsync("5");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save rules" }).ClickAsync();
        await Expect(StatusSaying(page, "Rules saved.")).ToBeVisibleAsync();
        await Expect(page.GetByLabel("Days after it was resolved")).ToHaveValueAsync("5");

        await page.GetByLabel("Close a ticket that has been resolved for a number of days").UncheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Save rules" }).ClickAsync();
        await Expect(StatusSaying(page, "Rules saved.")).ToBeVisibleAsync();
        await Expect(page.GetByLabel("Days after it was resolved")).ToHaveCountAsync(0);
    }

    [UiFact]
    public async Task Cancelling_before_the_reminder_is_refused_with_its_code_and_nothing_is_saved()
    {
        var page = await SignIn("admin@yasda.local");
        await page.GotoAsync("/automation");

        await page.GetByLabel("Email the requester when a ticket has waited for their answer").CheckAsync();
        await page.GetByLabel("Days waiting before the reminder").FillAsync("10");
        await page.GetByLabel("Cancel a ticket that has waited a long time for the requester").CheckAsync();
        await page.GetByLabel("Days waiting before the ticket is cancelled").FillAsync("5");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save rules" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("A ticket can't be cancelled before its reminder");
        await Expect(page.GetByLabel("Email the requester when a ticket has waited for their answer")).Not.ToBeCheckedAsync();
    }

    [UiFact]
    public async Task Someone_who_is_not_an_admin_neither_sees_nor_opens_the_automatic_rules()
    {
        var page = await SignIn("lead@yasda.local");

        await Expect(MainNav(page).GetByRole(AriaRole.Link, new() { Name = "Automatic rules" })).ToHaveCountAsync(0);
        await page.GotoAsync("/automation");
        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("You don't have access to this page.");
    }
}
