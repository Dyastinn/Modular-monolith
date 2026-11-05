using System.Security.Authentication;
using Erp.Modules.Identity.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Identity.Features;

// Anonymous but credential-checking: the CURRENT password is the proof. It has to work with an EXPIRED
// password — login refuses those and sends the user here.
internal sealed record ChangePasswordCommand(string Username, string CurrentPassword, string NewPassword) : ICommand;

internal sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
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

internal sealed class ChangePasswordHandler(IdentityDbContext db, PasswordHasher hasher, IClock clock)
    : ICommandHandler<ChangePasswordCommand>
{
    public async ValueTask<Unit> Handle(ChangePasswordCommand cmd, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Username == cmd.Username, ct);

        if (user is null || !user.IsActive || !hasher.Verify(cmd.CurrentPassword, user.PasswordHash))
            throw new AuthenticationException("Invalid username or password.");

        // Deliberately no expiry check here — fixing an expired password is what this endpoint is for.
        user.ChangePassword(hasher.Hash(cmd.NewPassword), clock.UtcNow);   // rotates the stamp → logs out everywhere
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
