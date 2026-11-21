using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Yasda.Web.UiTests;

public sealed class CategoryPolicyUiTests : UiTest
{
    [UiFact]
    public async Task A_team_lead_gives_a_category_its_own_numbers_and_takes_them_away_again()
    {
        var category = $"Outage {Guid.NewGuid():N}";
        var page = await SignIn("lead@yasda.local");
        await page.GotoAsync("/categories/new");
        await page.GetByLabel("Category name").FillAsync(category);
        await page.GetByRole(AriaRole.Button, new() { Name = "Create category" }).ClickAsync();
        await Expect(StatusSaying(page, "Category created.")).ToBeVisibleAsync();

        await MainNav(page).GetByRole(AriaRole.Link, new() { Name = "SLA policies" }).ClickAsync();
        await Expect(page.GetByText("No category has its own numbers yet.").Or(page.GetByRole(AriaRole.Region, new() { Name = "Category policies" }))).ToBeVisibleAsync();

        await page.GetByLabel("Category", new() { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = category });
        await page.GetByLabel("Priority", new() { Exact = true }).SelectOptionAsync("Critical");
        await page.GetByLabel("Respond within, in minutes").Last.FillAsync("15");
        await page.GetByLabel("Resolve within, in minutes").Last.FillAsync("60");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save category numbers" }).ClickAsync();
        await Expect(StatusSaying(page, "Category numbers saved.")).ToBeVisibleAsync();
        var row = page.GetByRole(AriaRole.Row).Filter(new() { HasText = category });
        await Expect(row.GetByRole(AriaRole.Cell, new() { Name = "15m", Exact = true })).ToBeVisibleAsync();

        await row.GetByRole(AriaRole.Button, new() { Name = $"Remove the Critical numbers of {category}" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Remove these numbers?");
        await page.GetByRole(AriaRole.Button, new() { Name = "Not yet" }).ClickAsync();
        await Expect(row).ToHaveCountAsync(1);

        await row.GetByRole(AriaRole.Button, new() { Name = $"Remove the Critical numbers of {category}" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = $"Yes, remove the Critical numbers of {category}" }).ClickAsync();
        await Expect(StatusSaying(page, "Category numbers removed.")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Row).Filter(new() { HasText = category })).ToHaveCountAsync(0);
    }
}
