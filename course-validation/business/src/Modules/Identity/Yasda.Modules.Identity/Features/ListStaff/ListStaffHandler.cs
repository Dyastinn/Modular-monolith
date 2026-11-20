using Dapper;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Identity.Features;

// Who can be assigned a ticket: every active staff account, or only the members of one team.
internal sealed record ListStaffQuery(long? TeamId = null);

internal sealed record StaffDto(long Id, string FullName, string Role);

internal sealed class ListStaffHandler(IDbConnectionFactory connections)
{
    public async Task<IReadOnlyList<StaffDto>> HandleAsync(ListStaffQuery query, CancellationToken ct)
    {
        await using var conn = connections.Create();
        var staff = await conn.QueryAsync<StaffDto>(new CommandDefinition(
            "SELECT * FROM identity.list_staff(NULL, @TeamId)", new { query.TeamId }, cancellationToken: ct));
        return staff.ToList();
    }
}
