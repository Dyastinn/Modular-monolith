using Yasda.Modules.Knowledge.Domain;

namespace Yasda.Modules.Knowledge.UnitTests;

// The people and the clock the article tests share.
internal static class Fixtures
{
    public static readonly DateTime Now = new(2026, 11, 21, 9, 0, 0, DateTimeKind.Utc);

    public static readonly Person Author = new(new UserId(3), "Alan Reyes");
    public static readonly Person OtherAgent = new(new UserId(7), "Oscar Dela Cruz");
    public static readonly Person Kira = new(new UserId(4), "Kira Santos");        // a KbEditor
    public static readonly Person OtherEditor = new(new UserId(5), "Ines Bautista");
    public static readonly Person Admin = new(new UserId(1), "Dyst");
    public static readonly Person Lead = new(new UserId(2), "Lena Torres");        // leads the author's team

    public static readonly ArticleAccess Nothing = new(IsEditor: false, LeadsAuthorsTeam: false);
    public static readonly ArticleAccess Editor = new(IsEditor: true, LeadsAuthorsTeam: false);
    public static readonly ArticleAccess TeamLead = new(IsEditor: false, LeadsAuthorsTeam: true);

    public static Article NewDraft()
    {
        return Article.Create("Reset your password", "Do it yourself in two minutes.", "Open the sign-in page.", ArticleVisibility.Public, Author, Now);
    }

    public static Article NewInReview()
    {
        var article = NewDraft();
        article.Submit(Author, Nothing, Now);
        return article;
    }

    // Written by the author, approved by Kira.
    public static Article NewPublished()
    {
        var article = NewInReview();
        article.Publish(Kira, actorIsAdmin: false, Now);
        return article;
    }
}
