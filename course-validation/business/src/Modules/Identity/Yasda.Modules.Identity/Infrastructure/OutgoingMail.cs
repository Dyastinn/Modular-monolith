namespace Yasda.Modules.Identity.Infrastructure;

// One mail waiting to go out, or already sent. The key is unique, which is what makes queueing the same mail twice
// harmless.
internal sealed class OutgoingMail
{
    public string Key { get; private set; } = default!;
    public string ToAddress { get; private set; } = default!;
    public string Subject { get; private set; } = default!;
    public string Body { get; private set; } = default!;
    public DateTime QueuedUtc { get; private set; }
    public DateTime? SentUtc { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    private OutgoingMail() { }

    public OutgoingMail(string key, string toAddress, string subject, string body, DateTime queuedUtc)
    {
        Key = key;
        ToAddress = toAddress;
        Subject = subject;
        Body = body;
        QueuedUtc = queuedUtc;
    }

    public void MarkSent(DateTime nowUtc)
    {
        SentUtc = nowUtc;
        LastError = null;
    }

    public void MarkFailed(string error)
    {
        Attempts++;
        LastError = error;
    }
}
