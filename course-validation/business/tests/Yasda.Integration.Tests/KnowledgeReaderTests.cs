using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Dapper;
using Yasda.Shared.BuildingBlocks;
using static Yasda.Integration.Tests.KnowledgeApi;

namespace Yasda.Integration.Tests;

// The readers' side of the knowledge base: what a requester can and cannot see, which version they see while an article is being
// revised, and how reading is counted.
[Collection("Integration")]
public class KnowledgeReaderTests(CustomWebAppFactory factory)
{
    private const string Agent = "agent@yasda.local", KbEditor = "editor@yasda.local", Requester = "requester@yasda.local";

    private async Task<long> ViewsOnAsync(long articleId)
    {
        await using var conn = factory.OpenDatabase();
        return await conn.ExecuteScalarAsync<long>("SELECT COALESCE(SUM(views), 0) FROM knowledge.article_view_day WHERE article_id = @articleId", new { articleId });
    }

    private async Task AddViewsAsync(long articleId, int views, int daysAgo = 0)
    {
        await using var conn = factory.OpenDatabase();
        await conn.ExecuteAsync(
            """
            INSERT INTO knowledge.article_view_day (article_id, day, views) VALUES (@articleId, current_date - @daysAgo, @views)
            ON CONFLICT (article_id, day) DO UPDATE SET views = knowledge.article_view_day.views + @views
            """, new { articleId, views, daysAgo });
    }

    private static async Task<List<long>> ReaderListAsync(HttpClient client, string search)
    {
        var page = await client.GetFromJsonAsync<PublishedPage>($"/api/kb/articles?search={search}&pageSize=100");
        return page!.Items.Select(r => r.Id).ToList();
    }

    // ---- What a requester sees ----

    [Fact]
    public async Task A_requester_finds_and_reads_a_published_public_article()
    {
        var agent = await factory.ClientFor(Agent);
        var editor = await factory.ClientFor(KbEditor);
        var requester = await factory.ClientFor(Requester);
        var title = UniqueTitle("Reset");
        var id = await CreatePublishedAsync(agent, editor, title);

        (await ReaderListAsync(requester, title)).Should().Equal(id);
        var article = await requester.GetFromJsonAsync<PublishedArticle>($"/api/kb/articles/{id}");

        article!.Title.Should().Be(title);
        article.Body.Should().Be("First paragraph.\n\nSecond paragraph.");
        article.VersionNumber.Should().Be(1);
    }

    [Fact]
    public async Task A_requester_never_sees_an_internal_article()
    {
        var agent = await factory.ClientFor(Agent);
        var editor = await factory.ClientFor(KbEditor);
        var requester = await factory.ClientFor(Requester);
        var title = UniqueTitle("Escalation");
        var id = await CreatePublishedAsync(agent, editor, title, visibility: "Internal");

        (await ReaderListAsync(requester, title)).Should().BeEmpty();
        (await requester.GetAsync($"/api/kb/articles/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReaderListAsync(agent, title)).Should().Equal(id);
        (await agent.GetAsync($"/api/kb/articles/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_requester_gets_404_for_a_draft_an_article_in_review_an_archived_one_and_a_never_published_one()
    {
        var agent = await factory.ClientFor(Agent);
        var editor = await factory.ClientFor(KbEditor);
        var requester = await factory.ClientFor(Requester);

        var draft = await CreateAsync(agent);
        var inReview = await CreateAsync(agent);
        await StepAsync(agent, inReview, "submit");
        var archived = await CreatePublishedAsync(agent, editor);
        await StepAsync(editor, archived, "archive");

        foreach (var id in new[] { draft, inReview, archived })
        {
            (await requester.GetAsync($"/api/kb/articles/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound, $"article {id}");
            (await agent.GetAsync($"/api/kb/articles/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound, $"staff don't see it as a reader either: article {id}");
        }

        (await StepAsync(editor, archived, "restore")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await requester.GetAsync($"/api/kb/articles/{archived}")).StatusCode.Should().Be(HttpStatusCode.OK, "restoring brings the published version back");
    }

    [Fact]
    public async Task Archiving_hides_the_article_from_readers_at_once()
    {
        var agent = await factory.ClientFor(Agent);
        var editor = await factory.ClientFor(KbEditor);
        var requester = await factory.ClientFor(Requester);
        var title = UniqueTitle("Archive");
        var id = await CreatePublishedAsync(agent, editor, title);
        (await ReaderListAsync(requester, title)).Should().Equal(id);

        await StepAsync(editor, id, "archive");

        (await ReaderListAsync(requester, title)).Should().BeEmpty();
    }

    // ---- Revising a published article ----

    [Fact]
    public async Task Readers_keep_the_old_version_until_the_new_one_is_published()
    {
        var agent = await factory.ClientFor(Agent);
        var editor = await factory.ClientFor(KbEditor);
        var requester = await factory.ClientFor(Requester);
        var oldTitle = UniqueTitle("Old");
        var newTitle = UniqueTitle("New");
        var id = await CreatePublishedAsync(agent, editor, oldTitle);

        (await EditAsync(agent, id, newTitle)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await GetAsync(agent, id)).Title.Should().Be(newTitle, "staff see the working copy");
        (await GetAsync(agent, id)).Status.Should().Be("Draft");
        var whileRevising = await requester.GetFromJsonAsync<PublishedArticle>($"/api/kb/articles/{id}");
        whileRevising!.Title.Should().Be(oldTitle);
        (await ReaderListAsync(requester, oldTitle)).Should().Equal(id);
        (await ReaderListAsync(requester, newTitle)).Should().BeEmpty();

        await SubmitAndPublishAsync(agent, editor, id);

        var afterwards = await requester.GetFromJsonAsync<PublishedArticle>($"/api/kb/articles/{id}");
        afterwards!.Title.Should().Be(newTitle);
        afterwards.VersionNumber.Should().Be(2);
        (await GetAsync(agent, id)).Versions.Select(v => v.Number).Should().Equal(2, 1);
    }

    [Fact]
    public async Task Switching_the_working_copy_to_internal_changes_nothing_for_readers_until_it_is_republished()
    {
        var agent = await factory.ClientFor(Agent);
        var editor = await factory.ClientFor(KbEditor);
        var requester = await factory.ClientFor(Requester);
        var id = await CreatePublishedAsync(agent, editor);

        await EditAsync(agent, id, visibility: "Internal");

        (await requester.GetAsync($"/api/kb/articles/{id}")).StatusCode.Should().Be(HttpStatusCode.OK, "the published version is still Public");

        await SubmitAndPublishAsync(agent, editor, id);

        (await requester.GetAsync($"/api/kb/articles/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_internal_article_made_public_stays_hidden_from_requesters_until_republished()
    {
        var agent = await factory.ClientFor(Agent);
        var editor = await factory.ClientFor(KbEditor);
        var requester = await factory.ClientFor(Requester);
        var id = await CreatePublishedAsync(agent, editor, visibility: "Internal");

        await EditAsync(agent, id, visibility: "Public");

        (await requester.GetAsync($"/api/kb/articles/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await SubmitAndPublishAsync(agent, editor, id);

        (await requester.GetAsync($"/api/kb/articles/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- Counting views ----

    [Fact]
    public async Task Reading_an_article_counts_one_view_per_visit()
    {
        var agent = await factory.ClientFor(Agent);
        var editor = await factory.ClientFor(KbEditor);
        var requester = await factory.ClientFor(Requester);
        var id = await CreatePublishedAsync(agent, editor);

        (await requester.PostAsync($"/api/kb/articles/{id}/view", content: null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await requester.PostAsync($"/api/kb/articles/{id}/view", content: null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ViewsOnAsync(id)).Should().Be(2);
    }

    [Fact]
    public async Task Opening_the_article_data_does_not_count_a_view_by_itself()
    {
        var agent = await factory.ClientFor(Agent);
        var editor = await factory.ClientFor(KbEditor);
        var requester = await factory.ClientFor(Requester);
        var id = await CreatePublishedAsync(agent, editor);

        await requester.GetAsync($"/api/kb/articles/{id}");

        (await ViewsOnAsync(id)).Should().Be(0);
    }

    [Fact]
    public async Task Two_readers_at_once_both_count()
    {
        var agent = await factory.ClientFor(Agent);
        var editor = await factory.ClientFor(KbEditor);
        var requester = await factory.ClientFor(Requester);
        var id = await CreatePublishedAsync(agent, editor);

        var visits = new List<Task<HttpResponseMessage>>();
        for (var i = 0; i < 20; i++)
            visits.Add(requester.PostAsync($"/api/kb/articles/{id}/view", content: null));
        var answers = await Task.WhenAll(visits);

        answers.Should().OnlyContain(a => a.StatusCode == HttpStatusCode.NoContent);
        (await ViewsOnAsync(id)).Should().Be(20);
    }

    [Fact]
    public async Task A_view_of_an_article_the_requester_cannot_see_is_a_404_and_counts_nothing()
    {
        var agent = await factory.ClientFor(Agent);
        var editor = await factory.ClientFor(KbEditor);
        var requester = await factory.ClientFor(Requester);
        var internalId = await CreatePublishedAsync(agent, editor, visibility: "Internal");
        var draftId = await CreateAsync(agent);

        (await requester.PostAsync($"/api/kb/articles/{internalId}/view", content: null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await requester.PostAsync($"/api/kb/articles/{draftId}/view", content: null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await requester.PostAsync("/api/kb/articles/999999999/view", content: null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await ViewsOnAsync(internalId)).Should().Be(0);
        (await ViewsOnAsync(draftId)).Should().Be(0);
    }

    // ---- Popular articles ----

    [Fact]
    public async Task Popular_lists_the_most_read_and_a_requester_only_gets_the_public_ones()
    {
        var agent = await factory.ClientFor(Agent);
        var editor = await factory.ClientFor(KbEditor);
        var requester = await factory.ClientFor(Requester);
        var publicOne = await CreatePublishedAsync(agent, editor);
        var internalOne = await CreatePublishedAsync(agent, editor, visibility: "Internal");
        await AddViewsAsync(publicOne, 5_000);
        await AddViewsAsync(internalOne, 6_000);

        var staffSees = (await agent.GetFromJsonAsync<List<Popular>>("/api/kb/popular"))!;
        var requesterSees = (await requester.GetFromJsonAsync<List<Popular>>("/api/kb/popular"))!;

        staffSees.Select(p => p.Id).Should().StartWith([internalOne, publicOne]);
        staffSees[0].Views.Should().Be(6_000);
        requesterSees.Select(p => p.Id).Should().NotContain(internalOne);
        requesterSees.Select(p => p.Id).Should().Contain(publicOne);
        requesterSees.Should().HaveCountLessThanOrEqualTo(5);
    }

    [Fact]
    public async Task Views_older_than_thirty_days_do_not_make_an_article_popular()
    {
        var agent = await factory.ClientFor(Agent);
        var editor = await factory.ClientFor(KbEditor);
        var id = await CreatePublishedAsync(agent, editor);
        await AddViewsAsync(id, 1_000_000, daysAgo: 45);

        var popular = await agent.GetFromJsonAsync<List<Popular>>("/api/kb/popular");

        popular!.Select(p => p.Id).Should().NotContain(id);
    }

    [Fact]
    public async Task An_archived_article_is_not_popular_however_much_it_was_read()
    {
        var agent = await factory.ClientFor(Agent);
        var editor = await factory.ClientFor(KbEditor);
        var id = await CreatePublishedAsync(agent, editor);
        await AddViewsAsync(id, 2_000_000);
        await StepAsync(editor, id, "archive");

        var popular = await agent.GetFromJsonAsync<List<Popular>>("/api/kb/popular");

        popular!.Select(p => p.Id).Should().NotContain(id);
    }

    // ---- The development seed ----

    [Fact]
    public async Task The_seed_publishes_its_articles_through_the_domain_so_requesters_see_only_the_public_one()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);

        var forRequester = await requester.GetFromJsonAsync<PublishedPage>("/api/kb/articles?pageSize=100");
        var forStaff = await agent.GetFromJsonAsync<PublishedPage>("/api/kb/articles?pageSize=100");

        forRequester!.Items.Select(r => r.Title).Should().Contain("Reset your password").And.NotContain("Escalating a network outage");
        forStaff!.Items.Select(r => r.Title).Should().Contain(["Reset your password", "Escalating a network outage"]);
        forStaff.Items.Select(r => r.Title).Should().NotContain("Set up the VPN on a home laptop", "that one is still a draft");
    }
}
