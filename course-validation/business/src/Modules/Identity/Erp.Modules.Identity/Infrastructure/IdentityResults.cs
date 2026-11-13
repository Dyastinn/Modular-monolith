using Erp.Shared.BuildingBlocks;
using Erp.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity;

namespace Erp.Modules.Identity.Infrastructure;

internal static class IdentityResults
{
    // Identity reports rule failures ("password too short", "email taken") as results; the API answers them as 422.
    public static void ThrowIfFailed(this IdentityResult result)
    {
        if (!result.Succeeded)
            throw new DomainException(IdentityErrors.AccountChangeRefused, string.Join(" ", result.Errors.Select(e => e.Description)));
    }

    public static async Task<int> CountActiveAdminsAsync(this UserManager<AppUser> users) =>
        (await users.GetUsersInRoleAsync(Roles.Admin)).Count(u => !u.IsDisabled);

    public static async Task<string> GetRoleAsync(this UserManager<AppUser> users, AppUser user) =>
        (await users.GetRolesAsync(user)).SingleOrDefault() ?? "";
}
