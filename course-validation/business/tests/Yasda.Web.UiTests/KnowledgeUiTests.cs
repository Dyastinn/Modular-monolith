using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Yasda.Web.UiTests;

public sealed class KnowledgeUiTests : UiTest
{
    private static ILocator Field(IPage page, string label) => page.GetByLabel(label, new() { Exact = true });

    private static string UniqueTitle(string start) => $"{start} {Guid.NewGuid():N}";

    // Writes a draft through the form, as an agent would, and leaves the page on the article's own page.
    private static async Task WriteDraft(IPage page, string title, string visibility = "Public")
    {
        await MainNav(page).GetByRole(AriaRole.Link, new() { Name = "Manage articles" }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "New article" }).ClickAsync();
        await Field(page, "Title").FillAsync(title);
        await Field(page, "Summary").FillAsync("Fix it yourself in two minutes.");
        await Field(page, "Body").FillAsync("Open the settings page.\n\nChoose the third option.");
        await Field(page, "Who can read it").SelectOptionAsync(visibility);
        await page.GetByRole(AriaRole.Button, new() { Name = "Save draft" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = title })).ToBeVisibleAsync();
        await Expect(page.GetByText("Status: Draft")).ToBeVisibleAsync();
    }

    private static async Task OpenInManageList(IPage page, string title)
    {
        await page.GotoAsync("/kb/manage?search=" + Uri.EscapeDataString(title));
        await page.GetByRole(AriaRole.Link, new() { Name = title }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = title })).ToBeVisibleAsync();
    }

    [UiFact]
    public async Task An_agent_drafts_an_editor_publishes_and_a_requester_finds_and_reads_it()
    {
        var title = UniqueTitle("Reset the printer");
        var agent = await SignIn("agent@yasda.local");
        await WriteDraft(agent, title);
        await agent.GetByRole(AriaRole.Button, new() { Name = "Submit for review" }).ClickAsync();
        await Expect(StatusSaying(agent, "Sent for review.")).ToBeVisibleAsync();
        await Expect(agent.GetByText("Status: In review")).ToBeVisibleAsync();

        var editor = await SignIn("editor@yasda.local");
        await OpenInManageList(editor, title);
        await editor.GetByRole(AriaRole.Button, new() { Name = "Publish" }).ClickAsync();
        await Expect(StatusSaying(editor, "Published.")).ToBeVisibleAsync();
        await Expect(editor.GetByText("Published version: 1")).ToBeVisibleAsync();

        var requester = await SignIn("requester@yasda.local");
        await requester.GetByRole(AriaRole.Searchbox, new() { Name = "Search the knowledge base" }).FillAsync(title);
        await requester.GetByRole(AriaRole.Button, new() { Name = "Search" }).ClickAsync();
        await requester.GetByRole(AriaRole.Link, new() { Name = title }).ClickAsync();
        await Expect(requester.GetByRole(AriaRole.Heading, new() { Name = title })).ToBeVisibleAsync();
        await Expect(requester.GetByText("Fix it yourself in two minutes.")).ToBeVisibleAsync();
        await Expect(requester.GetByText("Choose the third option.")).ToBeVisibleAsync();
        await Expect(requester.GetByText("Last updated")).ToBeVisibleAsync();
        await Expect(requester.GetByRole(AriaRole.Link, new() { Name = "Submit a ticket" })).ToBeVisibleAsync();
        await Shot(requester, "article");
    }

    [UiFact]
    public async Task A_requester_sees_only_public_articles_and_popular_ones_on_Home()
    {
        var agent = await SignIn("agent@yasda.local");
        await agent.GotoAsync("/kb");
        await Expect(agent.GetByRole(AriaRole.Link, new() { Name = "Reset your password" })).ToBeVisibleAsync();
        var internalLink = agent.GetByRole(AriaRole.Link, new() { Name = "Escalating a network outage" });
        await Expect(internalLink).ToBeVisibleAsync();
        await Expect(agent.GetByText("Internal: staff only")).ToBeVisibleAsync();
        var internalAddress = await internalLink.GetAttributeAsync("href");

        var requester = await SignIn("requester@yasda.local");
        await Expect(requester.GetByRole(AriaRole.Heading, new() { Name = "Popular articles" })).ToBeVisibleAsync();
        await Expect(requester.GetByRole(AriaRole.Link, new() { Name = "Reset your password" })).ToBeVisibleAsync();
        await requester.GotoAsync("/kb");
        await Expect(requester.GetByRole(AriaRole.Link, new() { Name = "Reset your password" })).ToBeVisibleAsync();
        await Expect(requester.GetByRole(AriaRole.Link, new() { Name = "Escalating a network outage" })).ToHaveCountAsync(0);
        await requester.GotoAsync("/" + internalAddress);
        await Expect(requester.GetByText("There is no such article")).ToBeVisibleAsync();
        await Expect(requester.GetByText("Staff only")).ToHaveCountAsync(0);
    }

    [UiFact]
    public async Task A_requester_has_no_way_into_the_staff_pages()
    {
        var requester = await SignIn("requester@yasda.local");

        await Expect(MainNav(requester).GetByRole(AriaRole.Link, new() { Name = "Manage articles" })).ToHaveCountAsync(0);
        await requester.GotoAsync("/kb/manage");
        await Expect(requester.GetByRole(AriaRole.Heading, new() { Name = "Manage articles" })).ToHaveCountAsync(0);
    }

    [UiFact]
    public async Task Sending_a_draft_back_shows_the_author_why()
    {
        var title = UniqueTitle("Map the shared drive");
        var agent = await SignIn("agent@yasda.local");
        await WriteDraft(agent, title);
        await agent.GetByRole(AriaRole.Button, new() { Name = "Submit for review" }).ClickAsync();
        await Expect(agent.GetByText("Status: In review")).ToBeVisibleAsync();

        var editor = await SignIn("editor@yasda.local");
        await OpenInManageList(editor, title);
        await editor.GetByRole(AriaRole.Button, new() { Name = "Send back" }).ClickAsync();
        await Field(editor, "What should change?").FillAsync("Add a screenshot of the drive letter");
        await editor.GetByRole(AriaRole.Button, new() { Name = "Send back" }).ClickAsync();
        await Expect(StatusSaying(editor, "Sent back.")).ToBeVisibleAsync();

        await OpenInManageList(agent, title);
        await Expect(agent.GetByText("Sent back for changes: Add a screenshot of the drive letter")).ToBeVisibleAsync();
        await Expect(agent.GetByText("Status: Draft")).ToBeVisibleAsync();
        await Expect(agent.GetByRole(AriaRole.Button, new() { Name = "Submit for review" })).ToBeVisibleAsync();
    }

    [UiFact]
    public async Task An_author_lets_a_colleague_edit_and_then_takes_it_back()
    {
        var title = UniqueTitle("Book a meeting room");
        var agent = await SignIn("agent@yasda.local");
        await WriteDraft(agent, title);

        await Field(agent, "Add a person").SelectOptionAsync(new SelectOptionValue { Label = "Lea Torres (Team lead)" });
        await agent.GetByRole(AriaRole.Button, new() { Name = "Add to the edit list" }).ClickAsync();
        await Expect(StatusSaying(agent, "Added to the edit list.")).ToBeVisibleAsync();
        var editList = agent.GetByRole(AriaRole.Region, new() { Name = "People on the edit list" });
        await Expect(editList.GetByRole(AriaRole.Cell, new() { Name = "Lea Torres", Exact = true })).ToBeVisibleAsync();

        await agent.GetByRole(AriaRole.Button, new() { Name = "Remove Lea Torres" }).ClickAsync();
        await Expect(StatusSaying(agent, "Removed from the edit list.")).ToBeVisibleAsync();
        await Expect(agent.GetByText("Nobody else yet.")).ToBeVisibleAsync();
    }

    [UiFact]
    public async Task Archiving_asks_first_and_then_hides_the_article_from_readers()
    {
        var title = UniqueTitle("Old printer guide");
        var agent = await SignIn("agent@yasda.local");
        await WriteDraft(agent, title);
        await agent.GetByRole(AriaRole.Button, new() { Name = "Submit for review" }).ClickAsync();
        await Expect(agent.GetByText("Status: In review")).ToBeVisibleAsync();

        var editor = await SignIn("editor@yasda.local");
        await OpenInManageList(editor, title);
        await editor.GetByRole(AriaRole.Button, new() { Name = "Publish" }).ClickAsync();
        await Expect(editor.GetByText("Status: Published")).ToBeVisibleAsync();
        await editor.GetByRole(AriaRole.Button, new() { Name = "Archive" }).ClickAsync();
        await Expect(editor.GetByText("Readers will no longer find it.")).ToBeVisibleAsync();
        await editor.GetByRole(AriaRole.Button, new() { Name = "Not yet" }).ClickAsync();
        await Expect(editor.GetByText("Status: Published")).ToBeVisibleAsync();
        await editor.GetByRole(AriaRole.Button, new() { Name = "Archive" }).ClickAsync();
        await editor.GetByRole(AriaRole.Button, new() { Name = "Archive" }).ClickAsync();
        await Expect(StatusSaying(editor, "Archived.")).ToBeVisibleAsync();
        await Expect(editor.GetByText("Status: Archived")).ToBeVisibleAsync();

        var requester = await SignIn("requester@yasda.local");
        await requester.GotoAsync("/kb?search=" + Uri.EscapeDataString(title));
        await Expect(requester.GetByText("No articles match.")).ToBeVisibleAsync();
    }

    [UiFact]
    public async Task The_knowledge_base_fits_a_phone_without_sideways_scrolling()
    {
        var requester = await SignIn("requester@yasda.local", width: 320);
        await requester.GotoAsync("/kb");
        await Expect(requester.GetByRole(AriaRole.Link, new() { Name = "Reset your password" })).ToBeVisibleAsync();

        var overflows = await requester.EvaluateAsync<bool>("document.documentElement.scrollWidth > document.documentElement.clientWidth");

        Assert.False(overflows, "the page must not scroll sideways at 320px");
    }
}
