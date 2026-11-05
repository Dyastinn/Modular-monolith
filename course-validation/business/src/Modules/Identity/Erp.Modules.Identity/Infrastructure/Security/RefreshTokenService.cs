using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Erp.Modules.Identity.Infrastructure;

// The random refresh token, its hash, and the sliding-window policy.
internal sealed class RefreshTokenService(IConfiguration config)
{
    private readonly int _slidingDays = int.Parse(config["Jwt:RefreshSlidingDays"] ?? "7");

    public (string RawToken, string TokenHash) Generate()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));   // 256 bits of entropy
        return (raw, Hash(raw));
    }

    // Plain SHA-256 is right here: the token is already long and random, so unlike a password it needs no salt
    // and no slow hash. We hash only so a database leak yields no usable tokens.
    public string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    // `from` + the sliding window, but never past `cap` (the password's expiry).
    public DateTime SlidingExpiry(DateTime from, DateTime cap)
    {
        var sliding = from.AddDays(_slidingDays);
        return sliding < cap ? sliding : cap;
    }
}
