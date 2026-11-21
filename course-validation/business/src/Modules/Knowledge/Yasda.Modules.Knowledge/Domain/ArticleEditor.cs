namespace Yasda.Modules.Knowledge.Domain;

// Someone who may edit this one article, though it isn't theirs: put on its edit list by the author, a team lead of the
// author's team, or a KbEditor or Admin.
internal sealed class ArticleEditor
{
    private ArticleEditor() { }

    internal ArticleEditor(Person person, Person addedBy, DateTime addedUtc)
    {
        UserId = person.Id;
        UserName = person.Name;
        AddedById = addedBy.Id;
        AddedByName = addedBy.Name;
        AddedUtc = addedUtc;
    }

    public long Id { get; private set; }
    public UserId UserId { get; private set; }
    public string UserName { get; private set; } = "";
    public UserId AddedById { get; private set; }
    public string AddedByName { get; private set; } = "";
    public DateTime AddedUtc { get; private set; }
}
