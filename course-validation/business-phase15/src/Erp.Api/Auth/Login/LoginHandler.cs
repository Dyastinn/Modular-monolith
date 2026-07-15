using System.Security.Authentication;
using Erp.Shared.Common;
using Erp.Shared.Common.Data;
using Erp.Shared.Infrastructure;
using Erp.Shared.Messaging;
using FluentValidation;

namespace Erp.Api.Auth;

public sealed record LoginCommand(string Username, string Password) : ICommand<LoginResult>;

// Returned by BOTH login and refresh: a fresh access token + the refresh token to store for next time.
public sealed record LoginResult(string AccessToken, string RefreshToken, string Username, string Role);

public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Username).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class LoginHandler(
    IUserRepository users,
    IPasswordHasher hasher,
    IJwtTokenService jwt,
    IRefreshTokenService refreshTokens,   // generates + hashes the random refresh token
    IRefreshTokenRepository store,        // persists it
    IDbConnectionFactory factory,
    IClock clock) : ICommandHandler<LoginCommand, LoginResult>
{
    public async Task<LoginResult> Handle(LoginCommand cmd, CancellationToken ct)
    {
        var user = await users.GetByUsernameAsync(cmd.Username, ct);

        // Same generic error whether the user is missing OR the password is wrong.
        if (user is null || !user.IsActive || !hasher.Verify(cmd.Password, user.PasswordHash))
            throw new AuthenticationException("Invalid username or password.");

        var now = clock.UtcNow;

        // The DB-driven password policy also governs login: an expired password can't mint a session.
        if (now >= user.PasswordExpiresUtc)
            throw new AuthenticationException("Your password has expired. Please reset it before signing in.");

        var access = jwt.CreateAccessToken(user.Id, user.Username, user.Role);

        // Start a NEW token family for this login, storing the refresh token's HASH (never the raw value).
        // The token carries the user's SecurityStamp, and its hard cap IS the password-expiry date (from the DB).
        var (rawRefresh, refreshHash) = refreshTokens.Generate();
        using var conn = factory.Create();
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        try
        {
            await store.AddAsync(refreshHash, user.Id, Guid.NewGuid(), user.SecurityStamp,
                refreshTokens.SlidingExpiry(now, user.PasswordExpiresUtc), user.PasswordExpiresUtc, tx, ct);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        return new LoginResult(access, rawRefresh, user.Username, user.Role);   // client keeps the RAW refresh token
    }
}
