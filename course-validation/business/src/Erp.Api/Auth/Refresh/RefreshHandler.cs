using System.Security.Authentication;
using Erp.Shared.Common;
using Erp.Shared.Common.Data;
using Erp.Shared.Infrastructure;
using Erp.Shared.Messaging;

namespace Erp.Api.Auth;

// Trade a refresh token for a new access token, rotating it and detecting replay of a stolen one (Phase 4 §9).
public sealed record RefreshCommand(string RefreshToken) : ICommand<LoginResult>;

public sealed class RefreshHandler(
    IRefreshTokenService refreshTokens,
    IRefreshTokenRepository store,
    IUserRepository users,
    IJwtTokenService jwt,
    IDbConnectionFactory factory,
    IClock clock) : ICommandHandler<RefreshCommand, LoginResult>
{
    public async Task<LoginResult> Handle(RefreshCommand cmd, CancellationToken ct)
    {
        var presentedHash = refreshTokens.Hash(cmd.RefreshToken);
        var token = await store.GetByHashAsync(presentedHash, ct)
            ?? throw new AuthenticationException("Invalid refresh token.");   // unknown token

        var now = clock.UtcNow;

        // REUSE DETECTION: this token was already rotated away, yet here it is again → someone copied it.
        // Burn the whole family so neither the thief nor the victim keeps using the lineage.
        if (token.RevokedUtc is not null)
        {
            using var breachConn = factory.Create();
            await breachConn.OpenAsync(ct);
            using var breach = breachConn.BeginTransaction();
            try
            {
                await store.RevokeFamilyAsync(token.FamilyId, breach, ct);
                breach.Commit();
            }
            catch
            {
                breach.Rollback();
                throw;
            }
            throw new AuthenticationException("Refresh token reuse detected — session revoked. Please log in again.");
        }

        // Expiry: BOTH the sliding window and the absolute cap (the password-expiry date) must be in the future.
        if (now >= token.ExpiresUtc || now >= token.AbsoluteExpiresUtc)
            throw new AuthenticationException("Refresh token expired. Please log in again.");

        // Reload the user fresh — a refresh is the moment to notice a role change or deactivation since login.
        var user = await users.GetByIdAsync(token.UserId, ct);
        if (user is null || !user.IsActive)
            throw new AuthenticationException("Account is no longer active.");

        // PASSWORD-CHANGE DETECTION: the stamp we snapshotted at mint no longer matches the user's current one,
        // so the password changed since this token was issued → every token in this lineage is now void.
        if (token.SecurityStamp != user.SecurityStamp)
            throw new AuthenticationException("Credentials changed. Please log in again.");

        // ROTATE: a new token in the SAME family. The sliding window resets (clamped to the password-expiry
        // cap); the hard cap is the user's CURRENT PasswordExpiresUtc — unchanged here, since the stamp matched.
        var (rawNew, hashNew) = refreshTokens.Generate();
        using var conn = factory.Create();
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        try
        {
            await store.AddAsync(hashNew, user.Id, token.FamilyId, user.SecurityStamp,
                refreshTokens.SlidingExpiry(now, user.PasswordExpiresUtc), user.PasswordExpiresUtc, tx, ct);
            await store.RevokeAsync(presentedHash, hashNew, tx, ct);   // old one spent
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        var access = jwt.CreateAccessToken(user.Id, user.Username, user.Role);
        return new LoginResult(access, rawNew, user.Username, user.Role);
    }
}

// Log out one session — anonymous: the access token may already be expired, and that's fine here.
public sealed record LogoutCommand(string RefreshToken) : ICommand<Unit>;

public sealed class LogoutHandler(
    IRefreshTokenService refreshTokens,
    IRefreshTokenRepository store,
    IDbConnectionFactory factory) : ICommandHandler<LogoutCommand, Unit>
{
    public async Task<Unit> Handle(LogoutCommand cmd, CancellationToken ct)
    {
        var token = await store.GetByHashAsync(refreshTokens.Hash(cmd.RefreshToken), ct);
        if (token is not null)   // unknown/already-gone token → nothing to do; logout is idempotent
        {
            using var conn = factory.Create();
            await conn.OpenAsync(ct);
            using var tx = conn.BeginTransaction();
            try
            {
                await store.RevokeFamilyAsync(token.FamilyId, tx, ct);   // this whole session out
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
        return Unit.Value;
    }
}

// "Log me out everywhere" — needs a valid ACCESS token, so it acts on the authenticated caller.
public sealed record LogoutAllCommand : ICommand<Unit>;

public sealed class LogoutAllHandler(
    ICurrentUser currentUser,
    IRefreshTokenRepository store,
    IDbConnectionFactory factory) : ICommandHandler<LogoutAllCommand, Unit>
{
    public async Task<Unit> Handle(LogoutAllCommand cmd, CancellationToken ct)
    {
        using var conn = factory.Create();
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        try
        {
            await store.RevokeAllForUserAsync(currentUser.UserId, tx, ct);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
        return Unit.Value;
    }
}
