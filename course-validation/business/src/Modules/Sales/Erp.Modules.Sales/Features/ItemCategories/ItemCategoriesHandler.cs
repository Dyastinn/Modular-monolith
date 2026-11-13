using Dapper;
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Sales.Features;

internal sealed record ItemCategoryDto(long Id, string Name, decimal MaxQuantityPerOrder, string? RequiredRole);

internal sealed class ListItemCategoriesHandler(IDbConnectionFactory connections)
{
    public async Task<IReadOnlyList<ItemCategoryDto>> HandleAsync(CancellationToken ct)
    {
        await using var conn = connections.Create();
        return (await conn.QueryAsync<ItemCategoryDto>(new CommandDefinition(
            "SELECT * FROM sales.list_item_categories();", cancellationToken: ct))).ToList();
    }
}

internal sealed record SaveItemCategoryCommand(long? CategoryId, string Name, decimal MaxQuantityPerOrder, string? RequiredRole);

// One command for create (no id) and edit (id): a category has nothing else to its life.
internal sealed class SaveItemCategoryHandler(SalesDbContext db)
{
    // null: the category to update doesn't exist.
    public async Task<long?> HandleAsync(SaveItemCategoryCommand cmd, CancellationToken ct)
    {
        var role = string.IsNullOrWhiteSpace(cmd.RequiredRole) ? null : cmd.RequiredRole;
        var name = cmd.Name.Trim();
        if (await db.ItemCategories.AnyAsync(c => c.Name == name && c.Id != new CategoryId(cmd.CategoryId ?? 0), ct))
            throw new DomainException(SalesErrors.CategoryNameTaken, $"A category named '{name}' already exists.");

        ItemCategory category;
        if (cmd.CategoryId is { } id)
        {
            var existing = await db.ItemCategories.FindAsync([new CategoryId(id)], ct);
            if (existing is null) return null;
            category = existing;
            category.Update(name, cmd.MaxQuantityPerOrder, role);
        }
        else
        {
            category = ItemCategory.Create(name, cmd.MaxQuantityPerOrder, role);
            db.ItemCategories.Add(category);
        }

        await db.SaveChangesAsync(ct);
        return category.Id.Value;
    }
}
