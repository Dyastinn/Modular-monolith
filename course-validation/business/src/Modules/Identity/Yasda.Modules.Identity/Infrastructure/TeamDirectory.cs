using Dapper;
using Yasda.Modules.Identity.Contracts;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Identity.Infrastructure;

internal sealed class TeamDirectory(IDbConnectionFactory connections) : ITeamDirectory
{
    public async Task<TeamInfo?> FindTeamAsync(long teamId, CancellationToken ct)
    {
        await using var conn = connections.Create();
        return await conn.QuerySingleOrDefaultAsync<TeamInfo>(new CommandDefinition(
            "SELECT id, name, is_active FROM identity.get_team(@teamId)", new { teamId }, cancellationToken: ct));
    }

    public async Task<long?> TeamIdOfAsync(string name, CancellationToken ct)
    {
        await using var conn = connections.Create();
        return await conn.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
            "SELECT id FROM identity.team WHERE lower(name) = lower(@name)", new { name }, cancellationToken: ct));
    }

    public async Task<bool> IsMemberAsync(long teamId, long userId, CancellationToken ct)
    {
        await using var conn = connections.Create();
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM identity.team_member WHERE team_id = @teamId AND user_id = @userId)",
            new { teamId, userId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<long>> TeamIdsOfAsync(long userId, CancellationToken ct)
    {
        await using var conn = connections.Create();
        var ids = await conn.QueryAsync<long>(new CommandDefinition(
            "SELECT team_id FROM identity.teams_of_user(@userId)", new { userId }, cancellationToken: ct));
        return ids.ToList();
    }

    public async Task<bool> LeadsTeamOfAsync(long leadUserId, long memberUserId, CancellationToken ct)
    {
        await using var conn = connections.Create();
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT identity.leads_team_of(@leadUserId, @memberUserId)", new { leadUserId, memberUserId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<long>> NotifyListAsync(long? teamId, CancellationToken ct)
    {
        await using var conn = connections.Create();
        var ids = await conn.QueryAsync<long>(new CommandDefinition(
            "SELECT user_id FROM identity.notify_list(@teamId)", new { teamId }, cancellationToken: ct));
        return ids.ToList();
    }
}
