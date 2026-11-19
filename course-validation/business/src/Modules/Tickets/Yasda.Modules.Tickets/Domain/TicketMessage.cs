namespace Yasda.Modules.Tickets.Domain;

// One entry in a ticket's conversation: the first request, a reply, or an internal note only staff can see.
internal sealed class TicketMessage
{
    private TicketMessage() { }

    internal TicketMessage(UserId authorId, string authorName, string body, bool isInternal, DateTime createdUtc) =>
        (AuthorId, AuthorName, Body, IsInternal, CreatedUtc) = (authorId, authorName, body, isInternal, createdUtc);

    public long Id { get; private set; }
    public UserId AuthorId { get; private set; }
    public string AuthorName { get; private set; } = "";
    public string Body { get; private set; } = "";
    public bool IsInternal { get; private set; }
    public DateTime CreatedUtc { get; private set; }
}
