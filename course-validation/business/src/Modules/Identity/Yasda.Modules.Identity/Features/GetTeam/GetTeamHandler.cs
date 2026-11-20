using Dapper;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Identity.Features;

internal sealed record TeamDetailDto(long Id, string Name, bool IsActive, string NotifyMode, IReadOnlyList<TeamMemberDto> Members);

internal sealed record TeamMemberDto(long UserId, string FullName, string Role, bool Notify, bool IsDisabled);

// One row of identity.get_team, exactly as the SQL function returns it.
internal sealed record TeamRow(long Id, string Name, bool IsActive, string NotifyMode);

internal sealed class GetTeamHandler(IDbConnectionFactory connections)
{
    public async Task<TeamDetailDto?> HandleAsync(long teamId, CancellationToken ct)
    {
        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM identity.get_team(@teamId);
            SELECT * FROM identity.get_team_members(@teamId);
            """, new { teamId }, cancellationToken: ct));

        var row = await multi.ReadSingleOrDefaultAsync<TeamRow>();
        if (row is null) return null;
        var members = (await multi.ReadAsync<TeamMemberDto>()).ToList();

        return new TeamDetailDto(row.Id, row.Name, row.IsActive, row.NotifyMode, members);
    }
}
