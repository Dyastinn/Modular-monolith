namespace Yasda.Modules.Knowledge.Domain;

// What readers saw from one publication on: the words and the visibility as they were when the article was published. Written
// once and never changed, so an old version can always be shown exactly as it was.
internal sealed class ArticleVersion
{
    private ArticleVersion() { }

    internal ArticleVersion(int number, string title, string summary, string body, ArticleVisibility visibility, Person publishedBy, DateTime publishedUtc)
    {
        Number = number;
        Title = title;
        Summary = summary;
        Body = body;
        Visibility = visibility;
        PublishedById = publishedBy.Id;
        PublishedByName = publishedBy.Name;
        PublishedUtc = publishedUtc;
    }

    public long Id { get; private set; }
    public int Number { get; private set; }
    public string Title { get; private set; } = "";
    public string Summary { get; private set; } = "";
    public string Body { get; private set; } = "";
    public ArticleVisibility Visibility { get; private set; }
    public UserId PublishedById { get; private set; }
    public string PublishedByName { get; private set; } = "";
    public DateTime PublishedUtc { get; private set; }
}
