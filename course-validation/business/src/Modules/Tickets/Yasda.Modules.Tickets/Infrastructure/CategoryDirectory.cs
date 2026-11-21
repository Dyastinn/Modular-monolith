using Dapper;
using Yasda.Modules.Tickets.Contracts;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Infrastructure;

internal sealed class CategoryDirectory(IDbConnectionFactory connections) : ICategoryDirectory
{
    public async Task<bool> ExistsAsync(long categoryId, CancellationToken ct)
    {
        await using var conn = connections.Create();
        var id = await conn.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
            "SELECT id FROM tickets.get_category(@categoryId)", new { categoryId }, cancellationToken: ct));
        return id is not null;
    }
}
