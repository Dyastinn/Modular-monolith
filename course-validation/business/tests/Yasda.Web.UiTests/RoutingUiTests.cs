using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Yasda.Web.UiTests;

public sealed class RoutingUiTests : UiTest
{
    private static ILocator Field(IPage page, string label) => page.GetByLabel(label, new() { Exact = true });

    // Files a ticket through the form as the requester does, choosing a category (or "Not sure").
    private async Task<(IPage Page, string Address)> FileTicket(string subject, string category)
    {
        var page = await SignIn("requester@yasda.local");
        await MainNav(page).GetByRole(AriaRole.Link, new() { Name = "New ticket" }).ClickAsync();
        await Field(page, "Subject").FillAsync(subject);
        await Field(page, "Details").FillAsync("It stopped working this morning.");
        await Field(page, "Category").SelectOptionAsync(new SelectOptionValue { Label = category });
        await page.GetByRole(AriaRole.Button, new() { Name = "Send ticket" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = subject })).ToBeVisibleAsync();
        return (page, new Uri(page.Url).PathAndQuery);
    }

    [UiFact]
    public async Task A_ticket_filed_with_a_category_lands_in_that_teams_queue_and_not_in_the_others()
    {
        var hardware = $"Monitor {Guid.NewGuid():N}";
        var network = $"Switch {Guid.NewGuid():N}";
        var (requester, _) = await FileTicket(hardware, "Hardware");
        await Expect(requester.GetByText("Category: Hardware")).ToBeVisibleAsync();
        await Expect(requester.GetByText("Team:")).ToHaveCountAsync(0);   // requesters see the category, not the routing
        await FileTicket(network, "Network");

        var agent = await SignIn("agent@yasda.local");
        await agent.GotoAsync("/tickets?team=mine&search=" + Uri.EscapeDataString(hardware));
        await Expect(agent.GetByRole(AriaRole.Link, new() { Name = hardware })).ToBeVisibleAsync();
        await agent.GotoAsync("/tickets?team=mine&search=" + Uri.EscapeDataString(network));
        await Expect(agent.GetByText("No tickets match.")).ToBeVisibleAsync();

        var lead = await SignIn("lead@yasda.local");
        await lead.GotoAsync("/tickets?search=" + Uri.EscapeDataString(network));
        await Expect(lead.GetByRole(AriaRole.Row).Filter(new() { HasText = network }).GetByText("Network")).ToBeVisibleAsync();
    }

    [UiFact]
    public async Task The_team_filter_is_offered_to_staff_and_remembered_in_the_address()
    {
        var page = await SignIn("agent@yasda.local");
        await page.GotoAsync("/tickets");

        await Field(page, "Team").SelectOptionAsync("mine");
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply" }).ClickAsync();

        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("team=mine"));
        await Expect(page.GetByText("Showing: my teams.")).ToBeVisibleAsync();

        var requester = await SignIn("requester@yasda.local");
        await requester.GotoAsync("/tickets");
        await Expect(requester.GetByLabel("Team", new() { Exact = true })).ToHaveCountAsync(0);
    }

    [UiFact]
    public async Task A_team_lead_routes_an_unsorted_ticket_and_an_owner_outside_the_new_team_loses_it()
    {
        var subject = $"Cable {Guid.NewGuid():N}";
        var (_, address) = await FileTicket(subject, "Not sure");
        var agent = await SignIn("agent@yasda.local");
        await agent.GotoAsync(address);
        await agent.GetByRole(AriaRole.Button, new() { Name = "Take it" }).ClickAsync();
        await Expect(agent.GetByText("Assigned: Alan Reyes")).ToBeVisibleAsync();

        var lead = await SignIn("lead@yasda.local");
        await lead.GotoAsync(address);
        await Expect(lead.GetByText("Team: None yet")).ToBeVisibleAsync();
        await Field(lead, "Category").SelectOptionAsync(new SelectOptionValue { Label = "Network" });
        await Expect(Field(lead, "Team")).ToHaveValueAsync(new System.Text.RegularExpressions.Regex("^[0-9]+$"));
        await lead.GetByRole(AriaRole.Button, new() { Name = "Route" }).ClickAsync();

        await Expect(StatusSaying(lead, "Routed.")).ToBeVisibleAsync();
        await Expect(lead.GetByText("Team: Network")).ToBeVisibleAsync();
        await Expect(lead.GetByText("Assigned: Nobody yet")).ToBeVisibleAsync();
        await Expect(lead.GetByText("Status: New")).ToBeVisibleAsync();
    }

    [UiFact]
    public async Task A_team_lead_manages_categories_and_an_agent_never_sees_the_pages()
    {
        var name = $"Phones {Guid.NewGuid():N}";
        var agent = await SignIn("agent@yasda.local");
        await Expect(MainNav(agent).GetByRole(AriaRole.Link, new() { Name = "Categories" })).ToHaveCountAsync(0);
        await agent.GotoAsync("/categories");
        await Expect(agent.GetByRole(AriaRole.Alert)).ToContainTextAsync("You don't have access to this page.");

        var lead = await SignIn("lead@yasda.local");
        await MainNav(lead).GetByRole(AriaRole.Link, new() { Name = "Categories" }).ClickAsync();
        await lead.GetByRole(AriaRole.Link, new() { Name = "New category" }).First.ClickAsync();
        await Field(lead, "Category name").FillAsync(name);
        await Field(lead, "Goes to team").SelectOptionAsync(new SelectOptionValue { Label = "Network" });
        await lead.GetByRole(AriaRole.Button, new() { Name = "Create category" }).ClickAsync();
        await Expect(lead.GetByRole(AriaRole.Heading, new() { Name = name })).ToBeVisibleAsync();
        await Expect(StatusSaying(lead, "Category created.")).ToBeVisibleAsync();

        await Field(lead, "Goes to team").SelectOptionAsync(new SelectOptionValue { Label = "Service desk" });
        await lead.GetByLabel("In use: people can choose it when they file a ticket").UncheckAsync();
        await lead.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await Expect(StatusSaying(lead, "Category saved.")).ToBeVisibleAsync();
        await Expect(lead.GetByText("Status: Switched off")).ToBeVisibleAsync();

        await lead.GotoAsync("/categories?search=" + Uri.EscapeDataString(name));
        var row = lead.GetByRole(AriaRole.Row).Filter(new() { HasText = name });
        await Expect(row.GetByText("Service desk")).ToBeVisibleAsync();
        await Expect(row.GetByText("Switched off")).ToBeVisibleAsync();
    }
}
