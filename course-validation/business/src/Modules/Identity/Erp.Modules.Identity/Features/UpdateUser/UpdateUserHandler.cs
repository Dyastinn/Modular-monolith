using System.Globalization;
using Erp.Modules.Identity.Domain;
using Erp.Modules.Identity.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Identity;

namespace Erp.Modules.Identity.Features;

internal sealed record UpdateUserCommand(long UserId, string FullName, string Role);

internal sealed class UpdateUserHandler(UserManager<AppUser> users, ICurrentUser current)
{
    public async Task<bool> HandleAsync(UpdateUserCommand cmd, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(cmd.UserId.ToString(CultureInfo.InvariantCulture));
        if (user is null) return false;
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
        return true;
    }
}

internal sealed record SetUserDisabledCommand(long UserId, bool Disabled);

internal sealed class SetUserDisabledHandler(UserManager<AppUser> users, ICurrentUser current)
{
    public async Task<bool> HandleAsync(SetUserDisabledCommand cmd, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(cmd.UserId.ToString(CultureInfo.InvariantCulture));
        if (user is null) return false;
        if (cmd.Disabled)
            StaffAccountRules.EnsureCanDisable(current.UserId, user.Id, await users.GetRoleAsync(user), await users.CountActiveAdminsAsync());

        user.IsDisabled = cmd.Disabled;
        (await users.UpdateAsync(user)).ThrowIfFailed();
        (await users.UpdateSecurityStampAsync(user)).ThrowIfFailed();   // ends every session they have
        return true;
    }
}

internal sealed record ResetUserPasswordCommand(long UserId, string TemporaryPassword);

internal sealed class ResetUserPasswordHandler(UserManager<AppUser> users)
{
    public async Task<bool> HandleAsync(ResetUserPasswordCommand cmd, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(cmd.UserId.ToString(CultureInfo.InvariantCulture));
        if (user is null) return false;
        var token = await users.GeneratePasswordResetTokenAsync(user);
        (await users.ResetPasswordAsync(user, token, cmd.TemporaryPassword)).ThrowIfFailed();
        (await users.SetLockoutEndDateAsync(user, null)).ThrowIfFailed();
        user.MustChangePassword = true;
        (await users.UpdateAsync(user)).ThrowIfFailed();
        return true;
    }
}
