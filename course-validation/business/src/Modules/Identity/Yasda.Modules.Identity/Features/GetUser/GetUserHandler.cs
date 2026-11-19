using Dapper;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Identity.Features;

internal sealed class GetUserHandler(IDbConnectionFactory connections)
{
    public async Task<UserDto?> HandleAsync(long id, CancellationToken ct)
    {
        await using var conn = connections.Create();
        return await conn.QuerySingleOrDefaultAsync<UserDto>(new CommandDefinition(
            "SELECT * FROM identity.get_user(@Id);", new { Id = id }, cancellationToken: ct));
    }
}
