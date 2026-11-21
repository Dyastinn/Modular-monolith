using AwesomeAssertions;
using Yasda.Modules.Knowledge.Domain;
using Yasda.Shared.BuildingBlocks;
using static Yasda.Modules.Knowledge.UnitTests.Fixtures;

namespace Yasda.Modules.Knowledge.UnitTests;

// An article's life: draft, review, published versions, archive. Each test names one rule.
public class ArticleLifeTests
{
    private static ErrorCode CodeOf(Action act)
    {
        var exception = FluentActions.Invoking(act).Should().Throw<DomainException>().Which;
        return exception.Error;
    }

    // ---- Creating ----

    [Fact]
    public void A_new_article_is_a_draft_nobody_has_published()
    {
        var article = NewDraft();

        article.Status.Should().Be(ArticleStatus.Draft);
        article.CurrentVersion.Should().Be(0);
        article.Versions.Should().BeEmpty();
        article.CreatedById.Should().Be(Author.Id);
        article.LastEditedById.Should().Be(Author.Id);
        article.History.Should().ContainSingle().Which.Action.Should().Be(ArticleAction.Created);
    }

    [Fact]
    public void The_words_are_trimmed()
    {
        var article = Article.Create("  Reset it  ", "  Two minutes.  ", "  Open the page.  ", ArticleVisibility.Public, Author, Now);

        article.Title.Should().Be("Reset it");
        article.Summary.Should().Be("Two minutes.");
        article.Body.Should().Be("Open the page.");
    }

    [Fact]
    public void Every_part_of_the_text_is_required()
    {
        CodeOf(() => Article.Create(" ", "s", "b", ArticleVisibility.Public, Author, Now)).Should().Be(KnowledgeErrors.TitleRequired);
        CodeOf(() => Article.Create("t", " ", "b", ArticleVisibility.Public, Author, Now)).Should().Be(KnowledgeErrors.SummaryRequired);
        CodeOf(() => Article.Create("t", "s", " ", ArticleVisibility.Public, Author, Now)).Should().Be(KnowledgeErrors.BodyRequired);
    }

    [Fact]
    public void The_text_has_a_limit_and_the_limit_itself_is_fine()
    {
        var atLimit = Article.Create(new string('t', Article.TitleMaxLength), new string('s', Article.SummaryMaxLength),
            new string('b', Article.BodyMaxLength), ArticleVisibility.Public, Author, Now);
        atLimit.Title.Should().HaveLength(Article.TitleMaxLength);

        CodeOf(() => Article.Create(new string('t', Article.TitleMaxLength + 1), "s", "b", ArticleVisibility.Public, Author, Now))
            .Should().Be(KnowledgeErrors.TitleTooLong);
        CodeOf(() => Article.Create("t", new string('s', Article.SummaryMaxLength + 1), "b", ArticleVisibility.Public, Author, Now))
            .Should().Be(KnowledgeErrors.SummaryTooLong);
        CodeOf(() => Article.Create("t", "s", new string('b', Article.BodyMaxLength + 1), ArticleVisibility.Public, Author, Now))
            .Should().Be(KnowledgeErrors.BodyTooLong);
    }

    // ---- Review ----

    [Fact]
    public void Submitting_a_draft_puts_it_in_review()
    {
        var article = NewDraft();

        article.Submit(Author, Nothing, Now);

        article.Status.Should().Be(ArticleStatus.InReview);
    }

    [Fact]
    public void Only_a_draft_can_be_submitted()
    {
        var inReview = NewInReview();
        var published = NewPublished();

        CodeOf(() => inReview.Submit(Author, Nothing, Now)).Should().Be(KnowledgeErrors.NotAllowedInThisStatus);
        CodeOf(() => published.Submit(Author, Nothing, Now)).Should().Be(KnowledgeErrors.NotAllowedInThisStatus);
    }

    [Fact]
    public void A_reason_of_exactly_the_longest_length_is_accepted()
    {
        var article = NewInReview();

        article.Reject(new string('x', Article.ReasonMaxLength), Kira, Now);

        article.Status.Should().Be(ArticleStatus.Draft);
    }

    [Fact]
    public void Sending_an_article_back_needs_a_reason_and_keeps_it()
    {
        var article = NewInReview();

        CodeOf(() => article.Reject("  ", Kira, Now)).Should().Be(KnowledgeErrors.RejectReasonRequired);
        CodeOf(() => article.Reject(new string('x', Article.ReasonMaxLength + 1), Kira, Now)).Should().Be(KnowledgeErrors.RejectReasonRequired);

        article.Reject("  Add a screenshot of the sign-in page  ", Kira, Now);

        article.Status.Should().Be(ArticleStatus.Draft);
        article.RejectionReason.Should().Be("Add a screenshot of the sign-in page");
        article.History[^1].Should().Match<ArticleHistoryEntry>(h => h.Action == ArticleAction.Rejected && h.Note == "Add a screenshot of the sign-in page");
    }

    [Fact]
    public void Submitting_again_clears_the_old_reason()
    {
        var article = NewInReview();
        article.Reject("Add a screenshot", Kira, Now);

        article.Submit(Author, Nothing, Now);

        article.RejectionReason.Should().BeNull();
    }

    [Fact]
    public void Only_an_article_in_review_can_be_sent_back()
    {
        CodeOf(() => NewDraft().Reject("No", Kira, Now)).Should().Be(KnowledgeErrors.NotAllowedInThisStatus);
        CodeOf(() => NewPublished().Reject("No", Kira, Now)).Should().Be(KnowledgeErrors.NotAllowedInThisStatus);
    }

    // ---- Publishing ----

    [Fact]
    public void Publishing_makes_the_working_copy_version_one()
    {
        var article = NewInReview();

        article.Publish(Kira, actorIsAdmin: false, Now.AddHours(1));

        article.Status.Should().Be(ArticleStatus.Published);
        article.CurrentVersion.Should().Be(1);
        var version = article.Versions.Should().ContainSingle().Which;
        version.Number.Should().Be(1);
        version.Title.Should().Be("Reset your password");
        version.Body.Should().Be("Open the sign-in page.");
        version.Visibility.Should().Be(ArticleVisibility.Public);
        version.PublishedById.Should().Be(Kira.Id);
        version.PublishedByName.Should().Be("Kira Santos");
        version.PublishedUtc.Should().Be(Now.AddHours(1));
        article.History[^1].Should().Match<ArticleHistoryEntry>(h => h.Action == ArticleAction.Published && h.VersionNumber == 1);
    }

    [Fact]
    public void Each_publication_adds_the_next_number_and_keeps_the_old_versions_as_they_were()
    {
        var article = NewPublished();
        article.Edit("Reset your password (new)", "Changed.", "Open the new page.", ArticleVisibility.Internal, Author, Nothing, Now);
        article.Submit(Author, Nothing, Now);
        article.Publish(OtherEditor, actorIsAdmin: false, Now);

        article.CurrentVersion.Should().Be(2);
        article.Versions.Select(v => v.Number).Should().Equal(1, 2);
        article.Versions[0].Title.Should().Be("Reset your password");
        article.Versions[0].Visibility.Should().Be(ArticleVisibility.Public);
        article.Versions[1].Title.Should().Be("Reset your password (new)");
        article.Versions[1].Visibility.Should().Be(ArticleVisibility.Internal);
    }

    [Fact]
    public void Only_an_article_in_review_can_be_published()
    {
        CodeOf(() => NewDraft().Publish(Kira, false, Now)).Should().Be(KnowledgeErrors.NotAllowedInThisStatus);
        CodeOf(() => NewPublished().Publish(OtherEditor, false, Now)).Should().Be(KnowledgeErrors.NotAllowedInThisStatus);
    }

    [Fact]
    public void Whoever_last_changed_the_text_cannot_publish_it()
    {
        var article = NewDraft();
        article.Edit("Reset your password", "Two minutes.", "Open the sign-in page. Then wait.", ArticleVisibility.Public, Kira, Editor, Now);
        article.Submit(Kira, Editor, Now);

        CodeOf(() => article.Publish(Kira, actorIsAdmin: false, Now)).Should().Be(KnowledgeErrors.CannotPublishOwnChanges);

        article.Status.Should().Be(ArticleStatus.InReview);
        article.Versions.Should().BeEmpty();
    }

    [Fact]
    public void The_author_who_became_an_editor_still_cannot_publish_their_own_draft()
    {
        var article = Article.Create("Reset", "Quick.", "Steps.", ArticleVisibility.Public, Kira, Now);
        article.Submit(Kira, Editor, Now);

        CodeOf(() => article.Publish(Kira, false, Now)).Should().Be(KnowledgeErrors.CannotPublishOwnChanges);
    }

    [Fact]
    public void An_admin_may_publish_their_own_changes_so_a_one_person_installation_is_not_stuck()
    {
        var article = Article.Create("Reset", "Quick.", "Steps.", ArticleVisibility.Public, Admin, Now);
        article.Submit(Admin, Editor, Now);

        article.Publish(Admin, actorIsAdmin: true, Now);

        article.Status.Should().Be(ArticleStatus.Published);
        article.History[^1].ActorName.Should().Be("Dyst");
    }

    [Fact]
    public void Another_editor_can_publish_what_the_first_one_wrote()
    {
        var article = Article.Create("Reset", "Quick.", "Steps.", ArticleVisibility.Public, Kira, Now);
        article.Submit(Kira, Editor, Now);

        article.Publish(OtherEditor, actorIsAdmin: false, Now);

        article.CurrentVersion.Should().Be(1);
    }

    // ---- Editing ----

    [Fact]
    public void Editing_a_draft_changes_the_working_copy_and_the_editor_of_record()
    {
        var article = NewDraft();

        article.Edit("New title", "New summary", "New body", ArticleVisibility.Internal, Kira, Editor, Now.AddMinutes(5));

        article.Title.Should().Be("New title");
        article.Visibility.Should().Be(ArticleVisibility.Internal);
        article.LastEditedById.Should().Be(Kira.Id);
        article.LastEditedByName.Should().Be("Kira Santos");
        article.CreatedById.Should().Be(Author.Id, "the author stays the author");
        article.Status.Should().Be(ArticleStatus.Draft);
    }

    [Fact]
    public void Editing_a_published_article_makes_it_a_draft_and_leaves_what_readers_see_alone()
    {
        var article = NewPublished();

        article.Edit("Different", "Different.", "Different words.", ArticleVisibility.Internal, Author, Nothing, Now);

        article.Status.Should().Be(ArticleStatus.Draft);
        article.CurrentVersion.Should().Be(1);
        var seenByReaders = article.Versions.Should().ContainSingle().Which;
        seenByReaders.Title.Should().Be("Reset your password");
        seenByReaders.Body.Should().Be("Open the sign-in page.");
        seenByReaders.Visibility.Should().Be(ArticleVisibility.Public);
    }

    [Fact]
    public void An_article_in_review_cannot_be_edited_until_it_is_sent_back()
    {
        var article = NewInReview();

        CodeOf(() => article.Edit("x", "y", "z", ArticleVisibility.Public, Author, Nothing, Now)).Should().Be(KnowledgeErrors.NotAllowedInThisStatus);

        article.Reject("Needs a screenshot", Kira, Now);
        article.Edit("x", "y", "z", ArticleVisibility.Public, Author, Nothing, Now);
        article.Title.Should().Be("x");
    }

    [Fact]
    public void An_edit_with_a_bad_title_changes_nothing()
    {
        var article = NewDraft();

        CodeOf(() => article.Edit(" ", "y", "z", ArticleVisibility.Internal, Author, Nothing, Now)).Should().Be(KnowledgeErrors.TitleRequired);

        article.Title.Should().Be("Reset your password");
        article.Visibility.Should().Be(ArticleVisibility.Public);
        article.History.Should().ContainSingle("a refused edit leaves no trace in the history");
    }

    // ---- Archive and restore ----

    [Fact]
    public void An_article_can_be_archived_from_any_status_and_keeps_its_versions()
    {
        ArticleStatus[] statuses = [ArticleStatus.Draft, ArticleStatus.InReview, ArticleStatus.Published];
        foreach (var from in statuses)
        {
            var article = ArticleIn(from);
            var versionsBefore = article.Versions.Count;

            article.Archive(Kira, Now);

            article.Status.Should().Be(ArticleStatus.Archived, $"it was {from}");
            article.Versions.Should().HaveCount(versionsBefore);
            article.History[^1].Action.Should().Be(ArticleAction.Archived);
        }
    }

    [Fact]
    public void An_archived_article_cannot_change_in_any_way()
    {
        var article = NewPublished();
        article.Archive(Kira, Now);

        CodeOf(() => article.Archive(Kira, Now)).Should().Be(KnowledgeErrors.ArticleArchived);
        CodeOf(() => article.Edit("x", "y", "z", ArticleVisibility.Public, Author, Nothing, Now)).Should().Be(KnowledgeErrors.ArticleArchived);
        CodeOf(() => article.Submit(Author, Nothing, Now)).Should().Be(KnowledgeErrors.ArticleArchived);
        CodeOf(() => article.Reject("No", Kira, Now)).Should().Be(KnowledgeErrors.ArticleArchived);
        CodeOf(() => article.Publish(OtherEditor, false, Now)).Should().Be(KnowledgeErrors.ArticleArchived);
    }

    [Fact]
    public void Restoring_brings_an_archived_article_back_as_a_draft()
    {
        var article = NewPublished();
        article.Archive(Kira, Now);

        article.Restore(Kira, Now);

        article.Status.Should().Be(ArticleStatus.Draft);
        article.CurrentVersion.Should().Be(1, "its published version is still there");
        article.History[^1].Action.Should().Be(ArticleAction.Restored);
    }

    [Fact]
    public void Only_an_archived_article_can_be_restored()
    {
        CodeOf(() => NewDraft().Restore(Kira, Now)).Should().Be(KnowledgeErrors.NotAllowedInThisStatus);
    }

    // ---- The audit trail ----

    [Fact]
    public void Every_successful_step_adds_one_history_row_with_who_and_when()
    {
        var article = NewDraft();
        article.Edit("A", "B", "C", ArticleVisibility.Public, Kira, Editor, Now.AddMinutes(1));
        article.Submit(Kira, Editor, Now.AddMinutes(2));
        article.Reject("Not clear", OtherEditor, Now.AddMinutes(3));
        article.Submit(Kira, Editor, Now.AddMinutes(4));
        article.Publish(OtherEditor, false, Now.AddMinutes(5));
        article.Archive(Admin, Now.AddMinutes(6));
        article.Restore(Admin, Now.AddMinutes(7));

        article.History.Select(h => h.Action).Should().Equal(
            ArticleAction.Created, ArticleAction.Edited, ArticleAction.Submitted, ArticleAction.Rejected,
            ArticleAction.Submitted, ArticleAction.Published, ArticleAction.Archived, ArticleAction.Restored);
        article.History.Select(h => h.ActorName).Should().Equal(
            "Alan Reyes", "Kira Santos", "Kira Santos", "Ines Bautista", "Kira Santos", "Ines Bautista", "Dyst", "Dyst");
        article.History[^1].AtUtc.Should().Be(Now.AddMinutes(7));
    }

    [Fact]
    public void A_refused_step_leaves_no_history_row()
    {
        var article = NewDraft();

        CodeOf(() => article.Publish(Kira, false, Now));
        CodeOf(() => article.Reject("No", Kira, Now));
        CodeOf(() => article.Restore(Kira, Now));

        article.History.Should().ContainSingle();
    }

    // Every change must touch the article's own row, or the database never notices that two people changed it at once.
    [Fact]
    public void Every_change_touches_the_article_so_a_stale_copy_is_refused()
    {
        var later = Now.AddHours(3);
        var changes = new Dictionary<string, Action<Article>>
        {
            ["edit"] = a => a.Edit("A", "B", "C", ArticleVisibility.Public, Author, Nothing, later),
            ["submit"] = a => a.Submit(Author, Nothing, later),
            ["reject"] = a => a.Reject("Why", Kira, later),
            ["publish"] = a => a.Publish(Kira, false, later),
            ["archive"] = a => a.Archive(Kira, later),
            ["restore"] = a => a.Restore(Kira, later),
            ["add editor"] = a => a.AddEditor(OtherAgent, Author, Nothing, later),
            ["remove editor"] = a => a.RemoveEditor(OtherAgent.Id, Author, Nothing, later),
        };
        var startingPoints = new Dictionary<string, Func<Article>>
        {
            ["edit"] = NewDraft,
            ["submit"] = NewDraft,
            ["reject"] = NewInReview,
            ["publish"] = NewInReview,
            ["archive"] = NewDraft,
            ["restore"] = () => { var a = NewDraft(); a.Archive(Kira, Now); return a; },
            ["add editor"] = NewDraft,
            ["remove editor"] = () => { var a = NewDraft(); a.AddEditor(OtherAgent, Author, Nothing, Now); return a; },
        };

        foreach (var (name, change) in changes)
        {
            var article = startingPoints[name]();

            change(article);

            article.UpdatedUtc.Should().Be(later, $"'{name}' must touch the article row");
        }
    }

    private static Article ArticleIn(ArticleStatus status)
    {
        if (status == ArticleStatus.Draft) return NewDraft();
        if (status == ArticleStatus.InReview) return NewInReview();
        return NewPublished();
    }
}
