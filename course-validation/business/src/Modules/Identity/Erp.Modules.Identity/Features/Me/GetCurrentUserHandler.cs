using Erp.Modules.Identity.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.AspNetCore.Identity;

namespace Erp.Modules.Identity.Features;

internal sealed record GetCurrentUserQuery : IQuery<CurrentUserDto>;

internal sealed record CurrentUserDto(long Id, string Username, string FullName, string Role, bool MustChangePassword);

internal sealed class GetCurrentUserHandler(ICurrentUser current, UserManager<AppUser> users)
    : IQueryHandler<GetCurrentUserQuery, CurrentUserDto>
{
    public async ValueTask<CurrentUserDto> Handle(GetCurrentUserQuery query, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(current.UserId.ToString())
            ?? throw new NotFoundException("The signed-in user no longer exists.");
        return new CurrentUserDto(user.Id, user.UserName!, user.FullName, current.Role, user.MustChangePassword);
    }
}
