using Erp.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Erp.Modules.Identity.Infrastructure;

// Identity's own sign-in, extended at its two documented hooks: a disabled account may not sign in, and every
// password attempt is recorded. Hashing, lockout and tokens stay entirely Identity's.
internal sealed class ErpSignInManager(
    UserManager<AppUser> users,
    IHttpContextAccessor context,
    IUserClaimsPrincipalFactory<AppUser> claims,
    IOptions<IdentityOptions> options,
    ILogger<SignInManager<AppUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<AppUser> confirmation,
    ErpIdentityDbContext db,
    IClock clock)
    : SignInManager<AppUser>(users, context, claims, options, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(AppUser user) =>
        !user.IsDisabled && await base.CanSignInAsync(user);

    public override async Task<SignInResult> PasswordSignInAsync(string userName, string password, bool isPersistent, bool lockoutOnFailure)
    {
        var user = await UserManager.FindByNameAsync(userName);
        var result = user is null
            ? SignInResult.Failed
            : await PasswordSignInAsync(user, password, isPersistent, lockoutOnFailure);

        var outcome = result.Succeeded ? "Succeeded"
            : result.IsLockedOut ? "LockedOut"
            : result.IsNotAllowed ? "NotAllowed"
            : result.RequiresTwoFactor ? "RequiresTwoFactor"
            : "InvalidCredentials";
        db.SignInAttempts.Add(new SignInAttempt(userName, user?.Id, outcome,
            Context.Connection.RemoteIpAddress?.ToString(), clock.UtcNow));
        await db.SaveChangesAsync();
        return result;
    }
}
