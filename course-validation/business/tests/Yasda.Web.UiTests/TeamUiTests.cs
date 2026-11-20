using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Yasda.Web.UiTests;

public sealed class TeamUiTests : UiTest
{
    [UiFact]
    public async Task An_admin_makes_a_team_adds_a_person_chooses_who_is_told_and_switches_it_off()
    {
        var name = $"Billing {Guid.NewGuid():N}";
        var page = await SignIn("admin@yasda.local");
        await MainNav(page).GetByRole(AriaRole.Link, new() { Name = "Teams" }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "New team" }).First.ClickAsync();
        await page.GetByLabel("Team name").FillAsync(name);
        await page.GetByRole(AriaRole.Button, new() { Name = "Create team" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = name })).ToBeVisibleAsync();
        await Expect(StatusSaying(page, "Team created.")).ToBeVisibleAsync();

        await page.GetByLabel("Add a person").SelectOptionAsync(new SelectOptionValue { Label = "Alan Reyes (Agent)" });
        await page.GetByRole(AriaRole.Button, new() { Name = "Add to team" }).ClickAsync();
        await Expect(StatusSaying(page, "Person added.")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Cell, new() { Name = "Alan Reyes", Exact = true })).ToBeVisibleAsync();

        await page.GetByLabel("Tell", new() { Exact = true }).SelectOptionAsync("LeadsAndSelected");
        await page.GetByLabel("Tell Alan Reyes").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Save who is told" }).ClickAsync();
        await Expect(StatusSaying(page, "Saved.")).ToBeVisibleAsync();
        await Expect(page.GetByLabel("Tell Alan Reyes")).ToBeCheckedAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Deactivate" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync($"Deactivate {name}?");
        await page.GetByRole(AriaRole.Button, new() { Name = "Not yet" }).ClickAsync();
        await Expect(page.GetByText("This team is active")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Deactivate" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Deactivate" }).ClickAsync();
        await Expect(StatusSaying(page, "Deactivated.")).ToBeVisibleAsync();
        await Expect(page.GetByText("Status: Inactive")).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Remove Alan Reyes" }).ClickAsync();
        await Expect(StatusSaying(page, "Alan Reyes removed.")).ToBeVisibleAsync();
        await Expect(page.GetByText("Nobody is in this team yet.")).ToBeVisibleAsync();
    }

    [UiFact]
    public async Task The_seeded_teams_are_listed_with_their_members()
    {
        var page = await SignIn("admin@yasda.local");
        await page.GotoAsync("/teams?search=Service desk");

        var row = page.GetByRole(AriaRole.Row).Filter(new() { HasText = "Service desk" });
        await Expect(row.GetByRole(AriaRole.Link, new() { Name = "Service desk" })).ToBeVisibleAsync();
        await Expect(row.GetByRole(AriaRole.Cell, new() { Name = "2", Exact = true })).ToBeVisibleAsync();
        await Expect(row.GetByText("Team leads only")).ToBeVisibleAsync();
    }

    [UiFact]
    public async Task Someone_who_is_not_an_admin_neither_sees_nor_opens_the_teams_pages()
    {
        var page = await SignIn("lead@yasda.local");

        await Expect(MainNav(page).GetByRole(AriaRole.Link, new() { Name = "Teams" })).ToHaveCountAsync(0);
        await page.GotoAsync("/teams");
        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("You don't have access to this page.");
    }
}
