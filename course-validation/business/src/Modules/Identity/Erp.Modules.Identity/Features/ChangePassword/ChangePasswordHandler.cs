using Erp.Modules.Identity.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.Identity;

namespace Erp.Modules.Identity.Features;

internal sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : ICommand;

internal sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().NotEqual(x => x.CurrentPassword)
            .WithMessage("The new password must be different from the current one.");
    }
}

// Like Identity's /manage/info, but it also clears "must change password" after a temporary password.
internal sealed class ChangePasswordHandler(UserManager<AppUser> users, SignInManager<AppUser> signIn, ICurrentUser current)
    : ICommandHandler<ChangePasswordCommand>
{
    public async ValueTask<Unit> Handle(ChangePasswordCommand cmd, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(current.UserId.ToString())
            ?? throw new NotFoundException("The signed-in user no longer exists.");

        (await users.ChangePasswordAsync(user, cmd.CurrentPassword, cmd.NewPassword)).ThrowIfFailed();
        user.MustChangePassword = false;
        (await users.UpdateAsync(user)).ThrowIfFailed();
        await signIn.RefreshSignInAsync(user);   // the new security stamp would end this session too; re-issue it
        return Unit.Value;
    }
}
