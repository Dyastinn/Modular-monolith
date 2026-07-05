using System.Data;
using Dapper;
using Erp.Modules.Sales.Domain;
using Erp.Shared.Infrastructure;

namespace Erp.Modules.Sales.Infrastructure;

public sealed class ItemRepository(IDbConnectionFactory factory) : IItemRepository
{
    private sealed record ItemRow(
        long ItemId, bool IsActive, decimal UnitPrice,
        long CategoryId, string CategoryName, int MaxQuantityPerOrder, string? RequiredRole);

    public async Task<IReadOnlyList<ItemForOrder>> GetForOrderAsync(IReadOnlyList<long> itemIds, CancellationToken ct)
    {
        using var conn = factory.Create();
        // The proc splits a CSV of ids (STRING_SPLIT) so we load all requested items in one round-trip.
        var csv = string.Join(',', itemIds);
        var rows = await conn.QueryAsync<ItemRow>(new CommandDefinition(
            "sales.usp_Item_GetByIds", new { ItemIds = csv },
            commandType: CommandType.StoredProcedure, cancellationToken: ct));

        return rows.Select(r => new ItemForOrder(
            r.ItemId, r.IsActive, new Money(r.UnitPrice),
            new ItemCategory(r.CategoryId, r.CategoryName, r.MaxQuantityPerOrder, r.RequiredRole))).ToList();
    }
}
