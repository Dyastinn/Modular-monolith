namespace Erp.Modules.Identity.Domain;

// One stored refresh token. We keep only its SHA-256 hash, so a leaked table yields nothing usable.
internal sealed class RefreshToken
{
    public long Id { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public long UserId { get; private set; }
    public Guid FamilyId { get; private set; }         // one login lineage; rotation keeps it
    public Guid SecurityStamp { get; private set; }    // the user's stamp when issued
    public DateTime ExpiresUtc { get; private set; }   // sliding window, reset on every rotation
    public DateTime AbsoluteExpiresUtc { get; private set; }   // hard cap = the password's expiry
    public DateTime CreatedUtc { get; private set; }
    public DateTime? RevokedUtc { get; private set; }
    public string? ReplacedByHash { get; private set; }

    private RefreshToken() { }   // for EF

    public static RefreshToken Issue(
        string tokenHash, long userId, Guid familyId, Guid securityStamp,
        DateTime nowUtc, DateTime expiresUtc, DateTime absoluteExpiresUtc) => new()
    {
        TokenHash = tokenHash,
        UserId = userId,
        FamilyId = familyId,
        SecurityStamp = securityStamp,
        CreatedUtc = nowUtc,
        ExpiresUtc = expiresUtc,
        AbsoluteExpiresUtc = absoluteExpiresUtc
    };

    public bool IsRevoked => RevokedUtc is not null;
    public bool IsExpired(DateTime nowUtc) => nowUtc >= ExpiresUtc || nowUtc >= AbsoluteExpiresUtc;

    public void Revoke(DateTime nowUtc, string? replacedByHash = null)
    {
        RevokedUtc = nowUtc;
        ReplacedByHash = replacedByHash;
    }
}
