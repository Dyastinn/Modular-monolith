using System.Security.Authentication;
using Erp.Modules.Identity.Domain;
using Erp.Modules.Identity.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Identity.Features;

// Trade a refresh token for a new access token, rotating it and detecting replay of a stolen one.
internal sealed record RefreshCommand(string RefreshToken) : ICommand<LoginResult>;

internal sealed class RefreshHandler(
    IdentityDbContext db,
    RefreshTokenService refreshTokens,
    JwtTokenService jwt,
    IClock clock) : ICommandHandler<RefreshCommand, LoginResult>
{
    public async ValueTask<LoginResult> Handle(RefreshCommand cmd, CancellationToken ct)
    {
        var presentedHash = refreshTokens.Hash(cmd.RefreshToken);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == presentedHash, ct)
            ?? throw new AuthenticationException("Invalid refresh token.");
        var now = clock.UtcNow;

        // REUSE: this token was already rotated away, yet here it is again → someone copied it.
        // Burn the whole family so neither the thief nor the victim keeps the lineage.
        if (token.IsRevoked)
            await RevokeFamilyAndThrow(token.FamilyId, now, ct);

        if (token.IsExpired(now))
            throw new AuthenticationException("Refresh token expired. Please log in again.");

        // Reload the user — a refresh is the moment to notice a role change or deactivation.
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == token.UserId, ct);
        if (user is null || !user.IsActive)
            throw new AuthenticationException("Account is no longer active.");

        // The password changed since this token was issued → the whole lineage is void.
        if (token.SecurityStamp != user.SecurityStamp)
            throw new AuthenticationException("Credentials changed. Please log in again.");

        // ROTATE: new token, same family, old one spent — one SaveChanges, so both happen or neither.
        var (rawNew, hashNew) = refreshTokens.Generate();
        db.RefreshTokens.Add(RefreshToken.Issue(
            hashNew, user.Id, token.FamilyId, user.SecurityStamp, now,
            refreshTokens.SlidingExpiry(now, user.PasswordExpiresUtc), user.PasswordExpiresUtc));
        token.Revoke(now, replacedByHash: hashNew);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request rotated this same token a moment ago (RevokedUtc is a concurrency token).
            // Two clients presenting one token at once is exactly what theft looks like.
            db.ChangeTracker.Clear();
            await RevokeFamilyAndThrow(token.FamilyId, now, ct);
        }

        return new LoginResult(jwt.CreateAccessToken(user.Id, user.Username, user.Role), rawNew, user.Username, user.Role);
    }

    private async Task RevokeFamilyAndThrow(Guid familyId, DateTime now, CancellationToken ct)
    {
        await db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedUtc, now), ct);
        throw new AuthenticationException("Refresh token reuse detected — session revoked. Please log in again.");
    }
}

// Log out one session. Anonymous: the access token may already be expired, and that's fine here.
internal sealed record LogoutCommand(string RefreshToken) : ICommand;

internal sealed class LogoutHandler(IdentityDbContext db, RefreshTokenService refreshTokens, IClock clock)
    : ICommandHandler<LogoutCommand>
{
    public async ValueTask<Unit> Handle(LogoutCommand cmd, CancellationToken ct)
    {
        var hash = refreshTokens.Hash(cmd.RefreshToken);
        var familyId = await db.RefreshTokens.Where(t => t.TokenHash == hash)
            .Select(t => (Guid?)t.FamilyId).SingleOrDefaultAsync(ct);

        if (familyId is not null)   // unknown token → nothing to do; logout is idempotent
        {
            var now = clock.UtcNow;
            await db.RefreshTokens
                .Where(t => t.FamilyId == familyId && t.RevokedUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedUtc, now), ct);
        }
        return Unit.Value;
    }
}

// "Log me out everywhere" — needs a valid ACCESS token, so it acts on the authenticated caller.
internal sealed record LogoutAllCommand : ICommand;

internal sealed class LogoutAllHandler(IdentityDbContext db, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<LogoutAllCommand>
{
    public async ValueTask<Unit> Handle(LogoutAllCommand cmd, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await db.RefreshTokens
            .Where(t => t.UserId == currentUser.UserId && t.RevokedUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedUtc, now), ct);
        return Unit.Value;
    }
}
