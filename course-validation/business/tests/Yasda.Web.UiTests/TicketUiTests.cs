using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Yasda.Web.UiTests;

public sealed class TicketUiTests : UiTest
{
    private static ILocator Field(IPage page, string label) => page.GetByLabel(label, new() { Exact = true });

    // Files a ticket through the form, as a requester would, and returns the ticket page's address.
    private async Task<(IPage Page, string Address)> FileTicket(string subject)
    {
        var page = await SignIn("requester@yasda.local");
        await MainNav(page).GetByRole(AriaRole.Link, new() { Name = "New ticket" }).ClickAsync();
        await Field(page, "Subject").FillAsync(subject);
        await Field(page, "Details").FillAsync("It shows paper jam, but there's no paper stuck.");
        await Field(page, "Priority").SelectOptionAsync("High");
        await page.GetByRole(AriaRole.Button, new() { Name = "Send ticket" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = subject })).ToBeVisibleAsync();
        await Expect(page.GetByText("Status: New")).ToBeVisibleAsync();
        return (page, new Uri(page.Url).PathAndQuery);
    }

    [UiFact]
    public async Task A_requester_files_a_ticket_and_lands_on_it()
    {
        var (page, _) = await FileTicket($"Printer {Guid.NewGuid():N}");

        await Expect(StatusSaying(page, "Ticket sent. We'll answer here.")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Article)).ToContainTextAsync("there's no paper stuck");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Take it" })).ToHaveCountAsync(0);   // not for requesters
        await Shot(page, "ticket-filed");
    }

    [UiFact]
    public async Task An_agent_takes_answers_and_notes_and_the_requester_sees_only_the_answer()
    {
        var (requester, address) = await FileTicket($"Scanner {Guid.NewGuid():N}");
        var agent = await SignIn("agent@yasda.local");
        await agent.GotoAsync(address);

        await agent.GetByRole(AriaRole.Button, new() { Name = "Take it" }).ClickAsync();
        await Expect(agent.GetByText("Assigned: Alan Reyes")).ToBeVisibleAsync();

        await Field(agent, "Your reply").FillAsync("Please try again now.");
        await agent.GetByRole(AriaRole.Button, new() { Name = "Send reply" }).ClickAsync();
        await Expect(StatusSaying(agent, "Reply sent.")).ToBeVisibleAsync();

        await Field(agent, "Your reply").FillAsync("Swapped the fuser; third time this month.");
        await agent.GetByLabel("Internal note: only staff will see it").CheckAsync();
        await agent.GetByRole(AriaRole.Button, new() { Name = "Add note" }).ClickAsync();
        await Expect(agent.GetByRole(AriaRole.Article).Filter(new() { HasText = "Internal note" })).ToBeVisibleAsync();
        await Shot(agent, "agent-view");

        await requester.ReloadAsync();
        await Expect(requester.GetByRole(AriaRole.Article).Filter(new() { HasText = "Please try again now." })).ToBeVisibleAsync();
        await Expect(requester.GetByText("Swapped the fuser")).ToHaveCountAsync(0);
    }

    [UiFact]
    public async Task The_queue_filters_through_its_address()
    {
        var page = await SignIn("agent@yasda.local");
        await page.GotoAsync("/tickets");

        await Field(page, "Assigned to").SelectOptionAsync("Nobody yet");
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply" }).ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex("assigned=unassigned"));
        await Expect(page.GetByText("Showing: assigned to nobody yet.")).ToBeVisibleAsync();
        var assigneeCells = page.GetByRole(AriaRole.Row).Locator("td:nth-child(6)");
        await Expect(assigneeCells.First).ToHaveTextAsync("Nobody yet");
    }

    [UiFact]
    public async Task Cancelling_asks_for_a_reason_first_and_not_yet_backs_out()
    {
        var (page, _) = await FileTicket($"Mouse {Guid.NewGuid():N}");

        await page.GetByRole(AriaRole.Button, new() { Name = "Cancel ticket" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Nobody will work on it any more");
        await page.GetByRole(AriaRole.Button, new() { Name = "Not yet" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);

        await page.GetByRole(AriaRole.Button, new() { Name = "Cancel ticket" }).ClickAsync();
        await Field(page, "Why?").FillAsync("Fixed itself");
        await page.GetByRole(AriaRole.Button, new() { Name = "Cancel ticket" }).ClickAsync();

        await Expect(page.GetByText("Status: Cancelled")).ToBeVisibleAsync();
        await Expect(page.GetByText("Cancelled because: Fixed itself")).ToBeVisibleAsync();
    }

    [UiFact]
    public async Task The_home_page_offers_to_submit_a_ticket()
    {
        var page = await SignIn("requester@yasda.local");

        await page.GetByRole(AriaRole.Link, new() { Name = "Submit a ticket" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "New ticket" })).ToBeVisibleAsync();
    }

    // The link in an error message must work even for someone whose session just ended, so the page is public.
    [UiFact]
    public async Task The_error_code_page_explains_a_code_to_anyone()
    {
        var page = await NewPage();

        await page.GotoAsync("/errors/TICKETS-0005");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "TICKETS-0005: This ticket is closed" })).ToBeVisibleAsync();
        await Expect(page.GetByText("What to do:")).ToBeVisibleAsync();
    }
}
