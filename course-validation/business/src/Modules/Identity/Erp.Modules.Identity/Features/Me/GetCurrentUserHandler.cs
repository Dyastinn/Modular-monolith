using System.Globalization;
using Erp.Modules.Identity.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Identity;

namespace Erp.Modules.Identity.Features;

internal sealed record CurrentUserDto(long Id, string Username, string FullName, string Role, bool MustChangePassword);

internal sealed class GetCurrentUserHandler(ICurrentUser current, UserManager<AppUser> users)
{
    public async Task<CurrentUserDto> HandleAsync(CancellationToken ct)
    {
        var user = await users.FindByIdAsync(current.UserId.ToString(CultureInfo.InvariantCulture))
            ?? throw new InvalidOperationException("The signed-in user no longer exists.");
        return new CurrentUserDto(user.Id, user.UserName!, user.FullName, current.Role, user.MustChangePassword);
    }
}
