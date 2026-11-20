using Dapper;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Features;

internal sealed class GetCategoryHandler(IDbConnectionFactory connections)
{
    public async Task<CategoryDto?> HandleAsync(long categoryId, CancellationToken ct)
    {
        await using var conn = connections.Create();
        return await conn.QuerySingleOrDefaultAsync<CategoryDto>(new CommandDefinition(
            "SELECT * FROM tickets.get_category(@categoryId)", new { categoryId }, cancellationToken: ct));
    }
}
