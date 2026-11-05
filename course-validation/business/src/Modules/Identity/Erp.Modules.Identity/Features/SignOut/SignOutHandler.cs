using Erp.Modules.Identity.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.AspNetCore.Identity;

namespace Erp.Modules.Identity.Features;

internal sealed record SignOutCommand : ICommand;

internal sealed class SignOutHandler(SignInManager<AppUser> signIn) : ICommandHandler<SignOutCommand>
{
    public async ValueTask<Unit> Handle(SignOutCommand cmd, CancellationToken ct)
    {
        await signIn.SignOutAsync();   // clears the browser's sign-in cookie
        return Unit.Value;
    }
}

// A new security stamp invalidates every cookie and refresh token the user holds, on every device.
internal sealed record SignOutEverywhereCommand : ICommand;

internal sealed class SignOutEverywhereHandler(UserManager<AppUser> users, SignInManager<AppUser> signIn, ICurrentUser currentUser)
    : ICommandHandler<SignOutEverywhereCommand>
{
    public async ValueTask<Unit> Handle(SignOutEverywhereCommand cmd, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(currentUser.UserId.ToString())
            ?? throw new NotFoundException("User not found.");
        await users.UpdateSecurityStampAsync(user);
        await signIn.SignOutAsync();
        return Unit.Value;
    }
}
