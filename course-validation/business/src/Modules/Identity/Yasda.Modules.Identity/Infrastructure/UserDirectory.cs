using Dapper;
using Yasda.Modules.Identity.Contracts;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Identity.Infrastructure;

internal sealed class UserDirectory(IDbConnectionFactory connections) : IUserDirectory
{
    public async Task<long?> IdOfAsync(string email, CancellationToken ct)
    {
        await using var conn = connections.Create();
        return await conn.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
            "SELECT id FROM identity.users WHERE normalized_email = upper(@email)", new { email }, cancellationToken: ct));
    }

    public async Task<string?> NameOfAsync(long userId, CancellationToken ct)
    {
        await using var conn = connections.Create();
        return await conn.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT full_name FROM identity.users WHERE id = @userId", new { userId }, cancellationToken: ct));
    }

    public async Task<StaffMember?> FindStaffAsync(long userId, CancellationToken ct)
    {
        await using var conn = connections.Create();
        return await conn.QuerySingleOrDefaultAsync<StaffMember>(new CommandDefinition(
            "SELECT * FROM identity.list_staff(@userId, NULL)", new { userId }, cancellationToken: ct));
    }
}
