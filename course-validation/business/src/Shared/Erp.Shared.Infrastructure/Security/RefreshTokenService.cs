using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Erp.Shared.Infrastructure;

// The random refresh token, its one-way hash, and the sliding/absolute expiry policy — in one place (Phase 4 §9).
public interface IRefreshTokenService
{
    (string RawToken, string TokenHash) Generate();   // a new random token + its hash
    string Hash(string rawToken);                       // hash an incoming token, to look it up
    DateTime SlidingExpiry(DateTime from);              // from + Jwt:RefreshSlidingDays
    DateTime AbsoluteExpiry(DateTime from);             // from + Jwt:RefreshAbsoluteDays
}

public sealed class RefreshTokenService(IConfiguration config) : IRefreshTokenService
{
    private readonly int _sliding  = int.Parse(config["Jwt:RefreshSlidingDays"]  ?? "7");
    private readonly int _absolute = int.Parse(config["Jwt:RefreshAbsoluteDays"] ?? "30");

    public (string, string) Generate()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));   // 256 bits of entropy
        return (raw, Hash(raw));
    }

    // A plain SHA-256 is right here: the token is already long and random, so — unlike a password — it needs
    // no salt and no slow hash. We hash only so a database leak yields no usable tokens.
    public string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    public DateTime SlidingExpiry(DateTime from)  => from.AddDays(_sliding);
    public DateTime AbsoluteExpiry(DateTime from) => from.AddDays(_absolute);
}
