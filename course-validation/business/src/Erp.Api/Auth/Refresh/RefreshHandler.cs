using System.Security.Authentication;
using Erp.Shared.Common;
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
    IUnitOfWork uow,
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
            await using var breach = await uow.BeginAsync(ct);
            await store.RevokeFamilyAsync(token.FamilyId, breach.Transaction, ct);
            await breach.CommitAsync(ct);
            throw new AuthenticationException("Refresh token reuse detected — session revoked. Please log in again.");
        }

        // Expiry: BOTH the sliding window and the absolute family cap must still be in the future.
        if (now >= token.ExpiresUtc || now >= token.AbsoluteExpiresUtc)
            throw new AuthenticationException("Refresh token expired. Please log in again.");

        // Reload the user fresh — a refresh is the moment to notice a role change or deactivation since login.
        var user = await users.GetByIdAsync(token.UserId, ct);
        if (user is null || !user.IsActive)
            throw new AuthenticationException("Account is no longer active.");

        // ROTATE: a new token in the SAME family; the sliding window resets, the absolute cap is carried over.
        var (rawNew, hashNew) = refreshTokens.Generate();
        await using var tx = await uow.BeginAsync(ct);
        await store.AddAsync(hashNew, user.Id, token.FamilyId,
            refreshTokens.SlidingExpiry(now), token.AbsoluteExpiresUtc, tx.Transaction, ct);
        await store.RevokeAsync(presentedHash, hashNew, tx.Transaction, ct);   // old one spent
        await tx.CommitAsync(ct);

        var access = jwt.CreateAccessToken(user.Id, user.Username, user.Role);
        return new LoginResult(access, rawNew, user.Username, user.Role);
    }
}

// Log out one session — anonymous: the access token may already be expired, and that's fine here.
public sealed record LogoutCommand(string RefreshToken) : ICommand<Unit>;

public sealed class LogoutHandler(
    IRefreshTokenService refreshTokens,
    IRefreshTokenRepository store,
    IUnitOfWork uow) : ICommandHandler<LogoutCommand, Unit>
{
    public async Task<Unit> Handle(LogoutCommand cmd, CancellationToken ct)
    {
        var token = await store.GetByHashAsync(refreshTokens.Hash(cmd.RefreshToken), ct);
        if (token is not null)   // unknown/already-gone token → nothing to do; logout is idempotent
        {
            await using var tx = await uow.BeginAsync(ct);
            await store.RevokeFamilyAsync(token.FamilyId, tx.Transaction, ct);   // this whole session out
            await tx.CommitAsync(ct);
        }
        return Unit.Value;
    }
}

// "Log me out everywhere" — needs a valid ACCESS token, so it acts on the authenticated caller.
public sealed record LogoutAllCommand : ICommand<Unit>;

public sealed class LogoutAllHandler(
    ICurrentUser currentUser,
    IRefreshTokenRepository store,
    IUnitOfWork uow) : ICommandHandler<LogoutAllCommand, Unit>
{
    public async Task<Unit> Handle(LogoutAllCommand cmd, CancellationToken ct)
    {
        await using var tx = await uow.BeginAsync(ct);
        await store.RevokeAllForUserAsync(currentUser.UserId, tx.Transaction, ct);
        await tx.CommitAsync(ct);
        return Unit.Value;
    }
}
