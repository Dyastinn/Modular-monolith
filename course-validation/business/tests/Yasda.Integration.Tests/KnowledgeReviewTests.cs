using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Yasda.Modules.Knowledge.Domain;
using Yasda.Modules.Knowledge.Infrastructure;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Yasda.Integration.Tests.KnowledgeApi;

namespace Yasda.Integration.Tests;

// The writers' side of the knowledge base through the real API and database: the review, who may change what, and what the
// audit trail remembers. Every test writes its own articles, because all the tests share one database.
[Collection("Integration")]
public class KnowledgeReviewTests(CustomWebAppFactory factory)
{
    private const string Admin = "admin@yasda.local", Lead = "lead@yasda.local", Agent = "agent@yasda.local",
        KbEditor = "editor@yasda.local", Requester = "requester@yasda.local";

    private async Task<HttpClient> NewAgentAsync()
    {
        return await factory.ClientFor(await factory.CreateUserAsync(Roles.Agent));
    }

    // ---- The review, start to finish ----

    [Fact]
    public async Task An_agent_drafts_an_article_and_a_kb_editor_publishes_it()
    {
        var agent = await factory.ClientFor(Agent);
        var editor = await factory.ClientFor(KbEditor);

        var id = await CreateAsync(agent);
        var draft = await GetAsync(agent, id);
        draft.Status.Should().Be("Draft");
        draft.CurrentVersion.Should().Be(0);

        (await StepAsync(agent, id, "submit")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetAsync(agent, id)).Status.Should().Be("InReview");
        (await StepAsync(editor, id, "publish")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var published = await GetAsync(agent, id);
        published.Status.Should().Be("Published");
        published.CurrentVersion.Should().Be(1);
        published.Versions.Should().ContainSingle().Which.PublishedByName.Should().Be("Kira Santos");
    }

    [Fact]
    public async Task Sending_an_article_back_needs_a_reason_and_shows_it_to_the_author()
    {
        var agent = await factory.ClientFor(Agent);
        var editor = await factory.ClientFor(KbEditor);
        var id = await CreateAsync(agent);
        await StepAsync(agent, id, "submit");

        var withoutReason = await editor.PostAsJsonAsync($"/api/articles/{id}/reject", new { reason = "" });
        withoutReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var sent = await editor.PostAsJsonAsync($"/api/articles/{id}/reject", new { reason = "Add the screenshot" });
        sent.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var article = await GetAsync(agent, id);
        article.Status.Should().Be("Draft");
        article.RejectionReason.Should().Be("Add the screenshot");
        article.History.Should().Contain(h => h.Action == "Rejected" && h.Note == "Add the screenshot" && h.ActorName == "Kira Santos");
    }

    [Fact]
    public async Task The_history_says_who_did_what_and_when()
    {
        var agent = await factory.ClientFor(Agent);
        var editor = await factory.ClientFor(KbEditor);
        var before = DateTime.UtcNow.AddMinutes(-1);

        var id = await CreatePublishedAsync(agent, editor);

        var history = (await GetAsync(agent, id)).History;
        history.Select(h => h.Action).Should().Equal("Created", "Submitted", "Published");
        history.Select(h => h.ActorName).Should().Equal("Alan Reyes", "Alan Reyes", "Kira Santos");
        history.Should().OnlyContain(h => h.AtUtc > before);
        history[2].VersionNumber.Should().Be(1);
    }

    [Fact]
    public async Task Whoever_last_changed_the_text_cannot_publish_it_but_an_admin_can()
    {
        var editor = await factory.ClientFor(KbEditor);
        var admin = await factory.ClientFor(Admin);

        var byEditor = await CreateAsync(editor);
        await StepAsync(editor, byEditor, "submit");
        var refused = await StepAsync(editor, byEditor, "publish");
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(refused)).Should().Be("KNOWLEDGE-0002");

        var byAdmin = await CreateAsync(admin);
        await StepAsync(admin, byAdmin, "submit");
        (await StepAsync(admin, byAdmin, "publish")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Archived_articles_cannot_be_edited_until_restored()
    {
        var agent = await factory.ClientFor(Agent);
        var editor = await factory.ClientFor(KbEditor);
        var id = await CreatePublishedAsync(agent, editor);

        (await StepAsync(editor, id, "archive")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var refused = await EditAsync(agent, id);
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(refused)).Should().Be("KNOWLEDGE-0003");

        (await StepAsync(editor, id, "restore")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await EditAsync(agent, id)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetAsync(agent, id)).Status.Should().Be("Draft");
    }

    [Fact]
    public async Task An_article_that_is_not_there_is_a_404_for_every_step()
    {
        var editor = await factory.ClientFor(KbEditor);

        (await editor.GetAsync("/api/articles/999999999")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await EditAsync(editor, 999999999)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await StepAsync(editor, 999999999, "publish")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await editor.PostAsJsonAsync("/api/articles/999999999/reject", new { reason = "No" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Who may use the staff endpoints ----

    [Fact]
    public async Task A_requester_cannot_call_any_staff_endpoint()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var id = await CreateAsync(agent);

        (await requester.GetAsync("/api/articles")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await requester.GetAsync($"/api/articles/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await requester.PostAsJsonAsync("/api/articles", new { title = "t", summary = "s", body = "b" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await EditAsync(requester, id)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await StepAsync(requester, id, "submit")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await requester.PostAsJsonAsync($"/api/articles/{id}/editors", new { userId = 1 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_agent_cannot_decide_a_review()
    {
        var agent = await factory.ClientFor(Agent);
        var id = await CreateAsync(agent);
        await StepAsync(agent, id, "submit");

        (await StepAsync(agent, id, "publish")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await agent.PostAsJsonAsync($"/api/articles/{id}/reject", new { reason = "No" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await StepAsync(agent, id, "archive")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await StepAsync(agent, id, "restore")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- Who may edit (decision F3) ----

    [Fact]
    public async Task Another_agent_cannot_edit_my_draft_but_can_read_it()
    {
        var author = await NewAgentAsync();
        var other = await NewAgentAsync();
        var id = await CreateAsync(author);

        var refused = await EditAsync(other, id);

        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(refused)).Should().Be("KNOWLEDGE-0004");
        (await GetAsync(other, id)).CanEdit.Should().BeFalse();
        (await GetAsync(author, id)).CanEdit.Should().BeTrue();
    }

    [Fact]
    public async Task A_kb_editor_may_edit_anyones_draft_and_the_article_remembers_who_changed_it()
    {
        var author = await NewAgentAsync();
        var editor = await factory.ClientFor(KbEditor);
        var id = await CreateAsync(author);

        (await EditAsync(editor, id)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var article = await GetAsync(author, id);
        article.LastEditedByName.Should().Be("Kira Santos");
        article.CreatedByName.Should().Be("Test User");
    }

    private async Task<(HttpClient Author, HttpClient Lead, long AuthorId, long LeadId)> AuthorAndLeadInOneTeamAsync()
    {
        var admin = await factory.ClientFor(Admin);
        var author = await NewAgentAsync();
        var lead = await factory.ClientFor(await factory.CreateUserAsync(Roles.TeamLead));
        var authorId = await IdOfAsync(author);
        var leadId = await IdOfAsync(lead);

        var team = await admin.PostAsJsonAsync("/api/teams", new { name = $"Writers {Guid.NewGuid():N}" });
        var teamId = (await team.Content.ReadFromJsonAsync<Created>())!.Id;
        (await admin.PostAsJsonAsync($"/api/teams/{teamId}/members", new { userId = authorId })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.PostAsJsonAsync($"/api/teams/{teamId}/members", new { userId = leadId })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return (author, lead, authorId, leadId);
    }

    [Fact]
    public async Task A_lead_of_the_authors_team_may_edit_the_draft()
    {
        var (author, lead, _, _) = await AuthorAndLeadInOneTeamAsync();
        var id = await CreateAsync(author);

        (await GetAsync(lead, id)).CanEdit.Should().BeTrue();
        (await EditAsync(lead, id)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetAsync(author, id)).History.Select(h => h.Action).Should().Contain("Edited");
    }

    [Fact]
    public async Task A_lead_of_another_team_may_not()
    {
        var (author, _, _, _) = await AuthorAndLeadInOneTeamAsync();
        var strangerLead = await factory.ClientFor(await factory.CreateUserAsync(Roles.TeamLead));
        var id = await CreateAsync(author);

        var refused = await EditAsync(strangerLead, id);

        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(refused)).Should().Be("KNOWLEDGE-0004");
        (await GetAsync(strangerLead, id)).CanEdit.Should().BeFalse();
    }

    [Fact]
    public async Task A_person_on_the_edit_list_may_edit_and_taking_them_off_ends_it()
    {
        var author = await NewAgentAsync();
        var friend = await NewAgentAsync();
        var friendId = await IdOfAsync(friend);
        var id = await CreateAsync(author);

        (await author.PostAsJsonAsync($"/api/articles/{id}/editors", new { userId = friendId })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await EditAsync(friend, id)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetAsync(author, id)).Editors.Should().ContainSingle().Which.UserId.Should().Be(friendId);

        (await author.DeleteAsync($"/api/articles/{id}/editors/{friendId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var refused = await EditAsync(friend, id);
        (await CodeOf(refused)).Should().Be("KNOWLEDGE-0004");
        (await GetAsync(author, id)).History.Select(h => h.Action).Should().Contain(["EditorAdded", "EditorRemoved"]);
    }

    [Fact]
    public async Task A_lead_of_the_authors_team_may_change_the_edit_list_but_someone_on_it_may_not()
    {
        var (author, lead, _, _) = await AuthorAndLeadInOneTeamAsync();
        var friend = await NewAgentAsync();
        var third = await NewAgentAsync();
        var id = await CreateAsync(author);

        (await lead.PostAsJsonAsync($"/api/articles/{id}/editors", new { userId = await IdOfAsync(friend) })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refused = await friend.PostAsJsonAsync($"/api/articles/{id}/editors", new { userId = await IdOfAsync(third) });
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(refused)).Should().Be("KNOWLEDGE-0004");
        (await GetAsync(friend, id)).CanManageEditors.Should().BeFalse();
        (await GetAsync(lead, id)).CanManageEditors.Should().BeTrue();
    }

    [Fact]
    public async Task Only_staff_can_be_put_on_the_edit_list()
    {
        var author = await NewAgentAsync();
        var requester = await factory.ClientFor(Requester);
        var id = await CreateAsync(author);

        var refused = await author.PostAsJsonAsync($"/api/articles/{id}/editors", new { userId = await IdOfAsync(requester) });

        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(refused)).Should().Be("KNOWLEDGE-0005");
        (await GetAsync(author, id)).Editors.Should().BeEmpty();
    }

    // ---- Two people at once ----

    [Fact]
    public async Task A_change_based_on_a_stale_copy_of_the_article_is_refused()
    {
        var agent = await factory.ClientFor(Agent);
        var id = await CreateAsync(agent);

        await using var alice = factory.Services.CreateAsyncScope();
        await using var bob = factory.Services.CreateAsyncScope();
        var aliceDb = alice.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
        var bobDb = bob.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
        var aliceCopy = await aliceDb.Articles.SingleAsync(a => a.Id == new ArticleId(id));
        var bobCopy = await bobDb.Articles.SingleAsync(a => a.Id == new ArticleId(id));
        var alan = new Person(new UserId(3), "Alan Reyes");
        var nothing = new ArticleAccess(IsEditor: false, LeadsAuthorsTeam: false);

        aliceCopy.AddEditor(new Person(new UserId(9), "Third Agent"), alan, nothing, DateTime.UtcNow);
        await aliceDb.SaveChangesAsync();
        bobCopy.Submit(new Person(new UserId(1), "Dyst"), new ArticleAccess(IsEditor: true, LeadsAuthorsTeam: false), DateTime.UtcNow);

        var bobSaves = () => bobDb.SaveChangesAsync();
        await bobSaves.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    // ---- Finding articles ----

    [Fact]
    public async Task The_staff_list_filters_sorts_and_pages()
    {
        var agent = await factory.ClientFor(Agent);
        var editor = await factory.ClientFor(KbEditor);
        var token = Guid.NewGuid().ToString("N");
        var banana = await CreateAsync(agent, $"Banana {token}");
        var apple = await CreatePublishedAsync(agent, editor, $"Apple {token}");
        var cherry = await CreateAsync(agent, $"Cherry {token}", visibility: "Internal");

        var byTitle = await agent.GetFromJsonAsync<StaffPage>($"/api/articles?search={token}&sort=title");
        byTitle!.Items.Select(r => r.Id).Should().Equal(apple, banana, cherry);
        var reversed = await agent.GetFromJsonAsync<StaffPage>($"/api/articles?search={token}&sort=title&desc=true");
        reversed!.Items.Select(r => r.Id).Should().Equal(cherry, banana, apple);

        var published = await agent.GetFromJsonAsync<StaffPage>($"/api/articles?search={token}&status=Published");
        published!.Items.Should().ContainSingle().Which.Id.Should().Be(apple);
        var internals = await agent.GetFromJsonAsync<StaffPage>($"/api/articles?search={token}&visibility=Internal");
        internals!.Items.Should().ContainSingle().Which.Id.Should().Be(cherry);

        var firstPage = await agent.GetFromJsonAsync<StaffPage>($"/api/articles?search={token}&sort=title&pageSize=2");
        firstPage!.Items.Should().HaveCount(2);
        firstPage.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task Mine_lists_what_I_wrote_and_what_I_may_edit_by_name()
    {
        var author = await NewAgentAsync();
        var friend = await NewAgentAsync();
        var mine = await CreateAsync(author);
        var shared = await CreateAsync(author);
        var notMine = await CreateAsync(author);
        await author.PostAsJsonAsync($"/api/articles/{shared}/editors", new { userId = await IdOfAsync(friend) });

        var authors = await author.GetFromJsonAsync<StaffPage>("/api/articles?mine=true&pageSize=100");
        authors!.Items.Select(r => r.Id).Should().Contain([mine, shared, notMine]);

        var friends = await friend.GetFromJsonAsync<StaffPage>("/api/articles?mine=true&pageSize=100");
        friends!.Items.Select(r => r.Id).Should().Equal(shared);
    }

    [Fact]
    public async Task A_filter_or_sort_the_list_does_not_know_is_a_400()
    {
        var agent = await factory.ClientFor(Agent);

        (await agent.GetAsync("/api/articles?status=Banana")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await agent.GetAsync("/api/articles?sort=body")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await agent.GetAsync("/api/articles?pageSize=0")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_form_refuses_empty_and_too_long_text()
    {
        var agent = await factory.ClientFor(Agent);

        var noTitle = await agent.PostAsJsonAsync("/api/articles", new { title = "", summary = "s", body = "b" });
        var longSummary = await agent.PostAsJsonAsync("/api/articles", new { title = "t", summary = new string('s', 301), body = "b" });

        noTitle.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        longSummary.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
