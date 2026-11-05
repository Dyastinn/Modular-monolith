using Erp.Shared.BuildingBlocks;
using Mediator;

namespace Erp.Modules.Identity.Features;

internal sealed record GetCurrentUserQuery : IQuery<CurrentUserDto>;

internal sealed record CurrentUserDto(long Id, string Username, string Role);

internal sealed class GetCurrentUserHandler(ICurrentUser user) : IQueryHandler<GetCurrentUserQuery, CurrentUserDto>
{
    public ValueTask<CurrentUserDto> Handle(GetCurrentUserQuery query, CancellationToken ct) =>
        ValueTask.FromResult(new CurrentUserDto(user.UserId, user.Username, user.Role));
}
