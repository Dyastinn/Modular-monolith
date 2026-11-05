namespace Erp.Modules.Identity.Domain;

// A person who can sign in. The password policy lives here, so it's testable without a database.
internal sealed class AppUser
{
    // A password is good for 90 days from the day it was set. A refresh token can never outlive it.
    public static readonly TimeSpan PasswordLifetime = TimeSpan.FromDays(90);

    public long Id { get; private set; }
    public string Username { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public string Role { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public Guid SecurityStamp { get; private set; }   // changes on every password change → old sessions die
    public DateTime PasswordExpiresUtc { get; private set; }

    private AppUser() { }   // for EF

    public bool IsPasswordExpired(DateTime nowUtc) => nowUtc >= PasswordExpiresUtc;

    // A new password restarts the 90 days and rotates the stamp, which invalidates every refresh token
    // issued before now — a password change logs the user out everywhere.
    public void ChangePassword(string newPasswordHash, DateTime nowUtc)
    {
        PasswordHash = newPasswordHash;
        SecurityStamp = Guid.NewGuid();
        PasswordExpiresUtc = nowUtc + PasswordLifetime;
    }
}
