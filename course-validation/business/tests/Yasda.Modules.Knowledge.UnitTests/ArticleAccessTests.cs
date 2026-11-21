using AwesomeAssertions;
using Yasda.Modules.Knowledge.Domain;
using Yasda.Shared.BuildingBlocks;
using static Yasda.Modules.Knowledge.UnitTests.Fixtures;

namespace Yasda.Modules.Knowledge.UnitTests;

// Who may change an article's words, and who may change the list of people who can: the author, a team lead of the author's
// team, a KbEditor or Admin, and (for the words only) anyone on the article's own edit list.
public class ArticleAccessTests
{
    private static ErrorCode CodeOf(Action act)
    {
        var exception = FluentActions.Invoking(act).Should().Throw<DomainException>().Which;
        return exception.Error;
    }

    private static void TryEdit(Article article, Person who, ArticleAccess access)
    {
        article.Edit("Changed", "Changed.", "Changed body.", ArticleVisibility.Public, who, access, Now);
    }

    [Fact]
    public void The_author_may_edit_their_own_draft()
    {
        var article = NewDraft();

        TryEdit(article, Author, Nothing);

        article.Title.Should().Be("Changed");
    }

    [Fact]
    public void Another_agent_may_read_a_draft_but_not_change_it()
    {
        var article = NewDraft();

        CodeOf(() => TryEdit(article, OtherAgent, Nothing)).Should().Be(KnowledgeErrors.NotAllowedToEdit);
        CodeOf(() => article.Submit(OtherAgent, Nothing, Now)).Should().Be(KnowledgeErrors.NotAllowedToEdit);

        article.Title.Should().Be("Reset your password");
    }

    [Fact]
    public void A_lead_of_the_authors_team_may_edit()
    {
        var article = NewDraft();

        TryEdit(article, Lead, TeamLead);

        article.LastEditedById.Should().Be(Lead.Id);
    }

    [Fact]
    public void A_kb_editor_or_admin_may_edit_anyones_draft()
    {
        var article = NewDraft();

        TryEdit(article, Kira, Editor);

        article.LastEditedById.Should().Be(Kira.Id);
    }

    [Fact]
    public void Someone_on_the_edit_list_may_edit_and_submit()
    {
        var article = NewDraft();
        article.AddEditor(OtherAgent, Author, Nothing, Now);

        TryEdit(article, OtherAgent, Nothing);
        article.Submit(OtherAgent, Nothing, Now);

        article.Status.Should().Be(ArticleStatus.InReview);
    }

    [Fact]
    public void Taking_someone_off_the_edit_list_ends_their_access()
    {
        var article = NewDraft();
        article.AddEditor(OtherAgent, Author, Nothing, Now);
        article.RemoveEditor(OtherAgent.Id, Author, Nothing, Now);

        CodeOf(() => TryEdit(article, OtherAgent, Nothing)).Should().Be(KnowledgeErrors.NotAllowedToEdit);
    }

    [Fact]
    public void Edit_access_does_not_let_someone_publish_what_they_wrote()
    {
        var article = NewDraft();
        article.AddEditor(OtherAgent, Author, Nothing, Now);
        TryEdit(article, OtherAgent, Nothing);
        article.Submit(OtherAgent, Nothing, Now);

        // Whoever last changed the words can not approve them, however they got the right to change them.
        CodeOf(() => article.Publish(OtherAgent, actorIsAdmin: false, Now)).Should().Be(KnowledgeErrors.CannotPublishOwnChanges);
    }

    // ---- The edit list ----

    [Fact]
    public void The_author_a_lead_and_an_editor_manage_the_edit_list()
    {
        var byAuthor = NewDraft();
        var byLead = NewDraft();
        var byEditor = NewDraft();

        byAuthor.AddEditor(OtherAgent, Author, Nothing, Now);
        byLead.AddEditor(OtherAgent, Lead, TeamLead, Now);
        byEditor.AddEditor(OtherAgent, Kira, Editor, Now);

        byAuthor.Editors.Should().ContainSingle().Which.UserId.Should().Be(OtherAgent.Id);
        byLead.Editors.Should().ContainSingle();
        byEditor.Editors.Should().ContainSingle().Which.AddedByName.Should().Be("Kira Santos");
    }

    [Fact]
    public void Someone_only_lent_edit_access_cannot_hand_it_on()
    {
        var article = NewDraft();
        article.AddEditor(OtherAgent, Author, Nothing, Now);
        var third = new Person(new UserId(9), "Third Agent");

        CodeOf(() => article.AddEditor(third, OtherAgent, Nothing, Now)).Should().Be(KnowledgeErrors.NotAllowedToEdit);
        CodeOf(() => article.RemoveEditor(OtherAgent.Id, OtherAgent, Nothing, Now)).Should().Be(KnowledgeErrors.NotAllowedToEdit);
    }

    [Fact]
    public void Adding_someone_twice_or_adding_the_author_changes_nothing()
    {
        var article = NewDraft();
        article.AddEditor(OtherAgent, Author, Nothing, Now);

        article.AddEditor(OtherAgent, Author, Nothing, Now);
        article.AddEditor(Author, Author, Nothing, Now);

        article.Editors.Should().ContainSingle();
        article.History.Count(h => h.Action == ArticleAction.EditorAdded).Should().Be(1);
    }

    [Fact]
    public void Removing_someone_who_is_not_on_the_list_changes_nothing()
    {
        var article = NewDraft();

        article.RemoveEditor(OtherAgent.Id, Author, Nothing, Now);

        article.History.Should().ContainSingle();
    }

    [Fact]
    public void The_history_names_who_was_added_and_removed()
    {
        var article = NewDraft();
        article.AddEditor(OtherAgent, Author, Nothing, Now);
        article.RemoveEditor(OtherAgent.Id, Lead, TeamLead, Now);

        var last = article.History.TakeLast(2).ToList();
        last[0].Should().Match<ArticleHistoryEntry>(h => h.Action == ArticleAction.EditorAdded && h.Note == "Oscar Dela Cruz" && h.ActorName == "Alan Reyes");
        last[1].Should().Match<ArticleHistoryEntry>(h => h.Action == ArticleAction.EditorRemoved && h.Note == "Oscar Dela Cruz" && h.ActorName == "Lena Torres");
    }
}
