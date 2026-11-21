using System.Net.Http.Json;
using AwesomeAssertions;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Yasda.Modules.Identity.Contracts;
using Yasda.Shared.BuildingBlocks;
using static Yasda.Integration.Tests.KnowledgeApi;

namespace Yasda.Integration.Tests;

// Decision E9: the review sends mail. Submitting tells the KbEditors, and sending back or publishing tells the author. The tests read
// identity.outgoing_mail once the outbox has delivered, and find each article's mail by its title.
[Collection("Integration")]
public class KnowledgeMailTests(CustomWebAppFactory factory)
{
    private const string Agent = "agent@yasda.local", KbEditor = "editor@yasda.local";

    private async Task<List<string>> RecipientsOfMailAbout(string title, string subjectStart)
    {
        await using var conn = factory.OpenDatabase();
        await Waiting.OutboxesAreDrainedAsync(conn);
        var addresses = await conn.QueryAsync<string>(
            "SELECT to_address FROM identity.outgoing_mail WHERE subject = @subject ORDER BY to_address", new { subject = $"{subjectStart}: {title}" });
        return addresses.ToList();
    }

    [Fact]
    public async Task Submitting_an_article_mails_the_kb_editors_and_not_the_author()
    {
        var agent = await factory.ClientFor(Agent);
        var title = UniqueTitle("Mail submit");
        var id = await CreateAsync(agent, title);

        (await StepAsync(agent, id, "submit")).EnsureSuccessStatusCode();

        var recipients = await RecipientsOfMailAbout(title, "Article waiting for review");
        recipients.Should().Contain(KbEditor);
        recipients.Should().NotContain(Agent);
        recipients.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Sending_an_article_back_mails_the_author_with_the_reason()
    {
        var agent = await factory.ClientFor(Agent);
        var editor = await factory.ClientFor(KbEditor);
        var title = UniqueTitle("Mail reject");
        var id = await CreateAsync(agent, title);
        (await StepAsync(agent, id, "submit")).EnsureSuccessStatusCode();

        var response = await editor.PostAsJsonAsync($"/api/articles/{id}/reject", new { reason = "Add a screenshot of the sign-in page" });
        response.EnsureSuccessStatusCode();

        (await RecipientsOfMailAbout(title, "Your article was sent back")).Should().Equal(Agent);
        await using var conn = factory.OpenDatabase();
        var body = await conn.QuerySingleAsync<string>("SELECT body FROM identity.outgoing_mail WHERE subject = @subject", new { subject = $"Your article was sent back: {title}" });
        body.Should().Contain("Add a screenshot of the sign-in page");
    }

    [Fact]
    public async Task Publishing_an_article_mails_the_author_once()
    {
        var agent = await factory.ClientFor(Agent);
        var editor = await factory.ClientFor(KbEditor);
        var title = UniqueTitle("Mail publish");
        var id = await CreateAsync(agent, title);
        (await StepAsync(agent, id, "submit")).EnsureSuccessStatusCode();

        (await StepAsync(editor, id, "publish")).EnsureSuccessStatusCode();

        (await RecipientsOfMailAbout(title, "Your article is published")).Should().Equal(Agent);
    }

    [Fact]
    public async Task Nobody_is_mailed_about_their_own_review_step()
    {
        var admin = await factory.ClientFor("admin@yasda.local");
        var title = UniqueTitle("Mail own");
        var id = await CreateAsync(admin, title);

        (await StepAsync(admin, id, "submit")).EnsureSuccessStatusCode();
        (await StepAsync(admin, id, "publish")).EnsureSuccessStatusCode();

        (await RecipientsOfMailAbout(title, "Article waiting for review")).Should().NotContain("admin@yasda.local");
        (await RecipientsOfMailAbout(title, "Your article is published")).Should().BeEmpty("the author published it themselves");
    }

    [Fact]
    public async Task The_directory_lists_the_active_accounts_of_a_role()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserDirectory>();
        var editor = await users.IdOfAsync(KbEditor, CancellationToken.None);

        var editors = await users.UsersInRoleAsync(Roles.KbEditor, CancellationToken.None);

        editors.Should().Contain(editor!.Value);
        editors.Should().NotContain((await users.IdOfAsync(Agent, CancellationToken.None))!.Value);
    }
}
