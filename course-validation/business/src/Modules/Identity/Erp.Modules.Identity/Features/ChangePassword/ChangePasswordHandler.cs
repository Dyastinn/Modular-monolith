using System.Globalization;
using Erp.Modules.Identity.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Identity;

namespace Erp.Modules.Identity.Features;

internal sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword);

// Like Identity's /manage/info, but it also clears "must change password" after a temporary password.
internal sealed class ChangePasswordHandler(UserManager<AppUser> users, SignInManager<AppUser> signIn, ICurrentUser current)
{
    public async Task HandleAsync(ChangePasswordCommand cmd, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(current.UserId.ToString(CultureInfo.InvariantCulture))
            ?? throw new InvalidOperationException("The signed-in user no longer exists.");

        (await users.ChangePasswordAsync(user, cmd.CurrentPassword, cmd.NewPassword)).ThrowIfFailed();
        user.MustChangePassword = false;
        (await users.UpdateAsync(user)).ThrowIfFailed();
        await signIn.RefreshSignInAsync(user);   // the new security stamp would end this session too; re-issue it
    }
}
