using Erp.Modules.Identity.Domain;
using Erp.Modules.Identity.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.Identity;

namespace Erp.Modules.Identity.Features;

internal sealed record UpdateUserCommand(long UserId, string FullName, string Role) : ICommand;

internal sealed class UpdateUserValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserValidator()
    {
        RuleFor(x => x.UserId).GreaterThan(0);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Role).NotEmpty();
    }
}

internal sealed class UpdateUserHandler(UserManager<AppUser> users, ICurrentUser current) : ICommandHandler<UpdateUserCommand>
{
    public async ValueTask<Unit> Handle(UpdateUserCommand cmd, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(cmd.UserId.ToString()) ?? throw new NotFoundException($"User {cmd.UserId} not found.");
        var currentRole = await users.GetRoleAsync(user);
        StaffAccountRules.EnsureCanChangeRole(current.UserId, user.Id, currentRole, cmd.Role, await users.CountActiveAdminsAsync());

        user.FullName = cmd.FullName.Trim();
        (await users.UpdateAsync(user)).ThrowIfFailed();
        if (currentRole != cmd.Role)
        {
            if (currentRole != "") (await users.RemoveFromRoleAsync(user, currentRole)).ThrowIfFailed();
            (await users.AddToRoleAsync(user, cmd.Role)).ThrowIfFailed();
            (await users.UpdateSecurityStampAsync(user)).ThrowIfFailed();   // their sessions carry the old role: end them
        }
        return Unit.Value;
    }
}

internal sealed record SetUserDisabledCommand(long UserId, bool Disabled) : ICommand;

internal sealed class SetUserDisabledHandler(UserManager<AppUser> users, ICurrentUser current) : ICommandHandler<SetUserDisabledCommand>
{
    public async ValueTask<Unit> Handle(SetUserDisabledCommand cmd, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(cmd.UserId.ToString()) ?? throw new NotFoundException($"User {cmd.UserId} not found.");
        if (cmd.Disabled)
            StaffAccountRules.EnsureCanDisable(current.UserId, user.Id, await users.GetRoleAsync(user), await users.CountActiveAdminsAsync());

        user.IsDisabled = cmd.Disabled;
        (await users.UpdateAsync(user)).ThrowIfFailed();
        (await users.UpdateSecurityStampAsync(user)).ThrowIfFailed();   // ends every session they have
        return Unit.Value;
    }
}

internal sealed record ResetUserPasswordCommand(long UserId, string TemporaryPassword) : ICommand;

internal sealed class ResetUserPasswordValidator : AbstractValidator<ResetUserPasswordCommand>
{
    public ResetUserPasswordValidator() => RuleFor(x => x.TemporaryPassword).NotEmpty();
}

internal sealed class ResetUserPasswordHandler(UserManager<AppUser> users) : ICommandHandler<ResetUserPasswordCommand>
{
    public async ValueTask<Unit> Handle(ResetUserPasswordCommand cmd, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(cmd.UserId.ToString()) ?? throw new NotFoundException($"User {cmd.UserId} not found.");
        var token = await users.GeneratePasswordResetTokenAsync(user);
        (await users.ResetPasswordAsync(user, token, cmd.TemporaryPassword)).ThrowIfFailed();
        (await users.SetLockoutEndDateAsync(user, null)).ThrowIfFailed();
        user.MustChangePassword = true;
        (await users.UpdateAsync(user)).ThrowIfFailed();
        return Unit.Value;
    }
}
