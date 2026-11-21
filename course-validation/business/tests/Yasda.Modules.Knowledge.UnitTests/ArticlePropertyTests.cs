using AwesomeAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Yasda.Modules.Knowledge.Domain;
using Yasda.Shared.BuildingBlocks;
using static Yasda.Modules.Knowledge.UnitTests.Fixtures;

namespace Yasda.Modules.Knowledge.UnitTests;

// Rules checked against hundreds of generated sequences, not only the ones we thought of.
public class ArticlePropertyTests
{
    // Whatever people do, in whatever order: published versions are numbered 1, 2, 3 without gaps and never change; a refused step
    // changes nothing; a successful one leaves exactly one history row; an archived article is frozen; and a published article's
    // working copy is exactly its newest version.
    [Property(MaxTest = 300)]
    public Property No_sequence_of_actions_breaks_the_articles_rules()
    {
        var actions = Gen.Elements("edit", "edit-as-stranger", "edit-as-editor", "submit", "reject", "publish", "publish-as-last-editor",
                "publish-as-admin", "archive", "restore")
            .ListOf()
            .ToArbitrary();

        return Prop.ForAll(actions, steps =>
        {
            var article = NewDraft();
            var copiesOfVersions = new List<(int Number, string Title, string Body, ArticleVisibility Visibility)>();
            var counter = 0;

            foreach (var step in steps)
            {
                counter = counter + 1;
                var historyBefore = article.History.Count;
                var statusBefore = article.Status;
                var titleBefore = article.Title;

                var succeeded = true;
                try
                {
                    Do(article, step, counter);
                }
                catch (DomainException)
                {
                    succeeded = false;
                }

                if (succeeded) article.History.Count.Should().Be(historyBefore + 1, $"'{step}' worked, so it is in the trail once");
                else article.History.Count.Should().Be(historyBefore, $"'{step}' was refused, so it leaves no trace");
                if (!succeeded) article.Status.Should().Be(statusBefore);
                if (statusBefore == ArticleStatus.Archived && step != "restore") article.Title.Should().Be(titleBefore, "an archived article is frozen");

                RememberNewVersions(article, copiesOfVersions);
                article.Versions.Select(v => v.Number).Should().Equal(Enumerable.Range(1, article.CurrentVersion), "versions count up without gaps");
                foreach (var copy in copiesOfVersions)
                {
                    var version = article.Versions.Single(v => v.Number == copy.Number);
                    version.Title.Should().Be(copy.Title, "a published version never changes");
                    version.Body.Should().Be(copy.Body);
                    version.Visibility.Should().Be(copy.Visibility);
                }
                if (article.Status == ArticleStatus.Published)
                {
                    var newest = article.Versions.Single(v => v.Number == article.CurrentVersion);
                    article.Title.Should().Be(newest.Title, "a published article has nothing waiting to be published");
                    article.Body.Should().Be(newest.Body);
                }
                if (article.Status == ArticleStatus.InReview) article.RejectionReason.Should().BeNull();
            }
        });
    }

    private static void RememberNewVersions(Article article, List<(int Number, string Title, string Body, ArticleVisibility Visibility)> copies)
    {
        foreach (var version in article.Versions)
        {
            var known = copies.Any(c => c.Number == version.Number);
            if (!known) copies.Add((version.Number, version.Title, version.Body, version.Visibility));
        }
    }

    // Each edit writes new words, so a changed version would show.
    private static void Do(Article article, string step, int counter)
    {
        var title = $"Title {counter}";
        var body = $"Body {counter}";
        var visibility = counter % 2 == 0 ? ArticleVisibility.Public : ArticleVisibility.Internal;
        switch (step)
        {
            case "edit": article.Edit(title, "Summary", body, visibility, Author, Nothing, Now); break;
            case "edit-as-stranger": article.Edit(title, "Summary", body, visibility, OtherAgent, Nothing, Now); break;
            case "edit-as-editor": article.Edit(title, "Summary", body, visibility, Kira, Editor, Now); break;
            case "submit": article.Submit(Author, Nothing, Now); break;
            case "reject": article.Reject("Please fix", OtherEditor, Now); break;
            case "publish": article.Publish(OtherEditor, actorIsAdmin: false, Now); break;
            case "publish-as-last-editor": article.Publish(new Person(article.LastEditedById, article.LastEditedByName), actorIsAdmin: false, Now); break;
            case "publish-as-admin": article.Publish(Admin, actorIsAdmin: true, Now); break;
            case "archive": article.Archive(Kira, Now); break;
            case "restore": article.Restore(Kira, Now); break;
        }
    }
}
