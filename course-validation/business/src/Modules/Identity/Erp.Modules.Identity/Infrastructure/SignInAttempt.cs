namespace Erp.Modules.Identity.Infrastructure;

// One row per password sign-in, successful or not: who, from where, and what happened. Never changed.
internal sealed class SignInAttempt
{
    public long Id { get; private set; }
    public string Email { get; private set; } = default!;
    public long? UserId { get; private set; }   // null when the email matched nobody
    public string Outcome { get; private set; } = default!;
    public string? IpAddress { get; private set; }
    public DateTime OccurredUtc { get; private set; }

    private SignInAttempt() { }

    public SignInAttempt(string email, long? userId, string outcome, string? ipAddress, DateTime occurredUtc)
    { Email = email; UserId = userId; Outcome = outcome; IpAddress = ipAddress; OccurredUtc = occurredUtc; }
}
