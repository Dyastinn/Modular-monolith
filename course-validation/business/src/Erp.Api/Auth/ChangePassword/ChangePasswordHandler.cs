using System.Security.Authentication;
using Erp.Shared.Common.Data;
using Erp.Shared.Infrastructure;
using Erp.Shared.Messaging;
using FluentValidation;

namespace Erp.Api.Auth;

// Change (or reset) a password. Anonymous BUT credential-verifying: the CURRENT password is the proof.
// That's deliberate — it's the one way a user whose password has EXPIRED can get back in, because login
// (§9) refuses an expired password and points them here. Unlike login, this handler does NOT reject on
// expiry. The X-Api-Key gate (§9b) still applies, exactly as it does to /auth/login.
public sealed record ChangePasswordCommand(string Username, string CurrentPassword, string NewPassword)
    : ICommand<Unit>;

public sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.Username).NotEmpty();
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8)
            .WithMessage("New password must be at least 8 characters.");
        RuleFor(x => x.NewPassword).NotEqual(x => x.CurrentPassword)
            .WithMessage("New password must be different from the current one.");
    }
}

public sealed class ChangePasswordHandler(
    IUserRepository users,
    IPasswordHasher hasher,
    IDbConnectionFactory factory) : ICommandHandler<ChangePasswordCommand, Unit>
{
    public async Task<Unit> Handle(ChangePasswordCommand cmd, CancellationToken ct)
    {
        var user = await users.GetByUsernameAsync(cmd.Username, ct);

        // Same generic error whether the user is missing OR the current password is wrong — never reveal which.
        if (user is null || !user.IsActive || !hasher.Verify(cmd.CurrentPassword, user.PasswordHash))
            throw new AuthenticationException("Invalid username or password.");

        // NOTE: deliberately no PasswordExpiresUtc check — an expired password is exactly what this endpoint
        // exists to fix, so the current credential must still get the user through.

        var newHash = hasher.Hash(cmd.NewPassword);

        // The proc rotates PasswordHash + SecurityStamp + PasswordExpiresUtc atomically. The new stamp
        // invalidates every outstanding refresh token on every device (an instant global logout), so the
        // client must log in again with the new password afterward.
        using var conn = factory.Create();
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        try
        {
            await users.ChangePasswordAsync(user.Id, newHash, tx, ct);
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
