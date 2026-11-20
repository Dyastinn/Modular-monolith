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

    // The seeded printer ticket was filed 30 hours ago with 4 hours to respond, so it is long past its deadline.
    [UiFact]
    public async Task The_queue_shows_staff_a_breached_deadline_in_words_and_requesters_no_deadline_at_all()
    {
        var agent = await SignIn("agent@yasda.local");
        await agent.GotoAsync("/tickets?search=" + Uri.EscapeDataString("Printer on the 3rd floor"));

        var row = agent.GetByRole(AriaRole.Row).Filter(new() { HasText = "Printer on the 3rd floor" });
        await Expect(row.GetByText("Breached")).ToBeVisibleAsync();
        await Expect(agent.GetByRole(AriaRole.Columnheader, new() { Name = "SLA" })).ToBeVisibleAsync();

        var requester = await SignIn("requester@yasda.local");
        await requester.GotoAsync("/tickets");
        await Expect(requester.GetByRole(AriaRole.Link, new() { Name = "Printer on the 3rd floor won't print" })).ToBeVisibleAsync();
        await Expect(requester.GetByRole(AriaRole.Columnheader, new() { Name = "SLA" })).ToHaveCountAsync(0);
        await Expect(requester.GetByText("Breached")).ToHaveCountAsync(0);
    }

    [UiFact]
    public async Task The_ticket_header_shows_the_SLA_to_staff_and_never_to_the_requester()
    {
        var agent = await SignIn("agent@yasda.local");
        await agent.GotoAsync("/tickets?search=VPN");
        await agent.GetByRole(AriaRole.Link, new() { Name = "Can't connect to the VPN from home" }).ClickAsync();
        await Expect(agent.GetByRole(AriaRole.Heading, new() { Name = "Can't connect to the VPN from home", Exact = false })).ToBeVisibleAsync();
        await Expect(agent.GetByText("SLA:")).ToBeVisibleAsync();

        var requester = await SignIn("requester@yasda.local");
        await requester.GotoAsync("/tickets?search=VPN");
        await requester.GetByRole(AriaRole.Link, new() { Name = "Can't connect to the VPN from home" }).ClickAsync();
        await Expect(requester.GetByRole(AriaRole.Heading, new() { Name = "Can't connect to the VPN from home", Exact = false })).ToBeVisibleAsync();
        await Expect(requester.GetByText("Status:")).ToBeVisibleAsync();
        await Expect(requester.GetByText("SLA:")).ToHaveCountAsync(0);
    }

    [UiFact]
    public async Task A_team_lead_changes_a_policy_on_its_page_and_an_agent_never_sees_the_page()
    {
        var agent = await SignIn("agent@yasda.local");
        await Expect(MainNav(agent).GetByRole(AriaRole.Link, new() { Name = "SLA policies" })).ToHaveCountAsync(0);

        var lead = await SignIn("lead@yasda.local");
        await MainNav(lead).GetByRole(AriaRole.Link, new() { Name = "SLA policies" }).ClickAsync();
        await Expect(lead.GetByRole(AriaRole.Heading, new() { Name = "SLA policies" })).ToBeVisibleAsync();

        var low = lead.GetByRole(AriaRole.Group, new() { Name = "Low" });
        var respond = low.GetByLabel("Respond within, in minutes");
        var resolve = low.GetByLabel("Resolve within, in minutes");
        var originalRespond = await respond.InputValueAsync();
        var originalResolve = await resolve.InputValueAsync();
        try
        {
            await respond.FillAsync("1500");
            await resolve.FillAsync("7500");
            await low.GetByRole(AriaRole.Button, new() { Name = "Save Low" }).ClickAsync();
            await Expect(StatusSaying(lead, "Low saved.")).ToBeVisibleAsync();
            await Expect(low.GetByText("That is 1d 1h.")).ToBeVisibleAsync();
        }
        finally
        {
            await respond.FillAsync(originalRespond);
            await resolve.FillAsync(originalResolve);
            await low.GetByRole(AriaRole.Button, new() { Name = "Save Low" }).ClickAsync();
        }
        await Expect(StatusSaying(lead, "Low saved.")).ToBeVisibleAsync();
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
    public async Task An_agent_takes_a_ticket_and_it_becomes_theirs()
    {
        var (_, address) = await FileTicket($"Projector {Guid.NewGuid():N}");
        var agent = await SignIn("agent@yasda.local");
        await agent.GotoAsync(address);

        await agent.GetByRole(AriaRole.Button, new() { Name = "Take it" }).ClickAsync();

        await Expect(StatusSaying(agent, "You have this ticket now.")).ToBeVisibleAsync();
        await Expect(agent.GetByText("Assigned: Alan Reyes")).ToBeVisibleAsync();
        await Expect(agent.GetByText("Status: Open")).ToBeVisibleAsync();
        await Expect(agent.GetByRole(AriaRole.Button, new() { Name = "Take it" })).ToHaveCountAsync(0);
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
    public async Task An_agent_works_a_ticket_to_resolved_and_the_requester_reopens_it()
    {
        var (requester, address) = await FileTicket($"Keyboard {Guid.NewGuid():N}");
        var agent = await SignIn("agent@yasda.local");
        await agent.GotoAsync(address);

        await agent.GetByRole(AriaRole.Button, new() { Name = "Take it" }).ClickAsync();
        await Expect(agent.GetByText("Status: Open")).ToBeVisibleAsync();
        await agent.GetByRole(AriaRole.Button, new() { Name = "Wait on requester" }).ClickAsync();
        await Expect(agent.GetByText("Status: Pending")).ToBeVisibleAsync();
        await agent.GetByRole(AriaRole.Button, new() { Name = "Resolve" }).ClickAsync();
        await Expect(agent.GetByText("Status: Resolved")).ToBeVisibleAsync();
        await Expect(agent.GetByRole(AriaRole.Button, new() { Name = "Close" })).ToBeVisibleAsync();

        await requester.ReloadAsync();
        await requester.GetByRole(AriaRole.Button, new() { Name = "Reopen" }).ClickAsync();
        await Expect(requester.GetByText("Status: Open")).ToBeVisibleAsync();
        await Expect(requester.GetByRole(AriaRole.Button, new() { Name = "Reopen" })).ToHaveCountAsync(0);
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

    [UiFact]
    public async Task A_column_heading_sorts_the_list_through_the_address()
    {
        var page = await SignIn("agent@yasda.local");
        await page.GotoAsync("/tickets");

        await page.GetByRole(AriaRole.Columnheader, new() { Name = "Priority" }).GetByRole(AriaRole.Link).ClickAsync();
        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("sort=priority"));
        await Expect(page.GetByRole(AriaRole.Columnheader, new() { Name = "Priority" })).ToHaveAttributeAsync("aria-sort", "ascending");

        await page.GetByRole(AriaRole.Columnheader, new() { Name = "Priority" }).GetByRole(AriaRole.Link).ClickAsync();
        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("desc=true"));
        await Expect(page.GetByRole(AriaRole.Columnheader, new() { Name = "Priority" })).ToHaveAttributeAsync("aria-sort", "descending");
    }
}
