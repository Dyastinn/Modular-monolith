using Dapper;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Sla.Features;

internal sealed record PolicyDto(string Priority, int RespondMinutes, int ResolveMinutes, DateTime UpdatedUtc, long UpdatedById, string UpdatedByName);

internal sealed class GetPoliciesHandler(IDbConnectionFactory connections)
{
    public async Task<List<PolicyDto>> HandleAsync(CancellationToken ct)
    {
        await using var conn = connections.Create();
        var rows = await conn.QueryAsync<PolicyDto>(new CommandDefinition("SELECT * FROM sla.get_policies()", cancellationToken: ct));
        return rows.ToList();
    }
}
