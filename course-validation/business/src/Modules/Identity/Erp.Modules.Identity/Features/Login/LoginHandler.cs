using System.Security.Authentication;
using Erp.Modules.Identity.Domain;
using Erp.Modules.Identity.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Identity.Features;

internal sealed record LoginCommand(string Username, string Password) : ICommand<LoginResult>;

// Returned by BOTH login and refresh: a fresh access token + the refresh token to keep for next time.
internal sealed record LoginResult(string AccessToken, string RefreshToken, string Username, string Role);

internal sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Username).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

internal sealed class LoginHandler(
    IdentityDbContext db,
    PasswordHasher hasher,
    JwtTokenService jwt,
    RefreshTokenService refreshTokens,
    IClock clock) : ICommandHandler<LoginCommand, LoginResult>
{
    public async ValueTask<LoginResult> Handle(LoginCommand cmd, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Username == cmd.Username, ct);

        // Same generic error whether the user is missing OR the password is wrong — never reveal which.
        if (user is null || !user.IsActive || !hasher.Verify(cmd.Password, user.PasswordHash))
            throw new AuthenticationException("Invalid username or password.");

        var now = clock.UtcNow;
        if (user.IsPasswordExpired(now))
            throw new AuthenticationException("Your password has expired. Please change it before signing in.");

        // Start a NEW token family for this login. Store the hash, hand the raw token to the client.
        var (rawRefresh, refreshHash) = refreshTokens.Generate();
        db.RefreshTokens.Add(RefreshToken.Issue(
            refreshHash, user.Id, Guid.NewGuid(), user.SecurityStamp, now,
            refreshTokens.SlidingExpiry(now, user.PasswordExpiresUtc), user.PasswordExpiresUtc));
        await db.SaveChangesAsync(ct);

        var access = jwt.CreateAccessToken(user.Id, user.Username, user.Role);
        return new LoginResult(access, rawRefresh, user.Username, user.Role);
    }
}
