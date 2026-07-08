using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Erp.Shared.Infrastructure;

// The random refresh token, its one-way hash, and the sliding-window policy — in one place (Phase 4 §9).
// There is deliberately NO AbsoluteExpiry(): a token's hard cap is the user's PasswordExpiresUtc (read from
// the DB by the handler), not a configured span — a session must never outlive its password.
public interface IRefreshTokenService
{
    (string RawToken, string TokenHash) Generate();          // a new random token + its hash
    string Hash(string rawToken);                            // hash an incoming token, to look it up
    DateTime SlidingExpiry(DateTime from, DateTime cap);     // from + Jwt:RefreshSlidingDays, but never past cap
}

public sealed class RefreshTokenService(IConfiguration config) : IRefreshTokenService
{
    private readonly int _sliding = int.Parse(config["Jwt:RefreshSlidingDays"] ?? "7");

    public (string, string) Generate()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));   // 256 bits of entropy
        return (raw, Hash(raw));
    }

    // A plain SHA-256 is right here: the token is already long and random, so — unlike a password — it needs
    // no salt and no slow hash. We hash only so a database leak yields no usable tokens.
    public string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    // The sliding window from 'from', CLAMPED so it never runs past the password-expiry cap. Keeps the
    // CK_RefreshToken_Expiry constraint (AbsoluteExpiresUtc >= ExpiresUtc) satisfied.
    public DateTime SlidingExpiry(DateTime from, DateTime cap)
    {
        var sliding = from.AddDays(_sliding);
        return sliding < cap ? sliding : cap;
    }
}
