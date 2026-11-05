using Dapper;
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Sales.Features;

internal sealed record ItemCategoryDto(long Id, string Name, decimal MaxQuantityPerOrder, string? RequiredRole);

internal sealed record ListItemCategoriesQuery : IQuery<IReadOnlyList<ItemCategoryDto>>;

internal sealed class ListItemCategoriesHandler(IDbConnectionFactory connections)
    : IQueryHandler<ListItemCategoriesQuery, IReadOnlyList<ItemCategoryDto>>
{
    public async ValueTask<IReadOnlyList<ItemCategoryDto>> Handle(ListItemCategoriesQuery q, CancellationToken ct)
    {
        await using var conn = connections.Create();
        return (await conn.QueryAsync<ItemCategoryDto>(new CommandDefinition(
            "SELECT * FROM sales.list_item_categories();", cancellationToken: ct))).ToList();
    }
}

internal sealed record SaveItemCategoryCommand(long? CategoryId, string Name, decimal MaxQuantityPerOrder, string? RequiredRole)
    : ICommand<long>;

internal sealed class SaveItemCategoryValidator : AbstractValidator<SaveItemCategoryCommand>
{
    public SaveItemCategoryValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.MaxQuantityPerOrder).GreaterThan(0);
    }
}

// One command for create (no id) and edit (id): a category has nothing else to its life.
internal sealed class SaveItemCategoryHandler(SalesDbContext db) : ICommandHandler<SaveItemCategoryCommand, long>
{
    public async ValueTask<long> Handle(SaveItemCategoryCommand cmd, CancellationToken ct)
    {
        var role = string.IsNullOrWhiteSpace(cmd.RequiredRole) ? null : cmd.RequiredRole;
        var name = cmd.Name.Trim();
        if (await db.ItemCategories.AnyAsync(c => c.Name == name && c.Id != new CategoryId(cmd.CategoryId ?? 0), ct))
            throw new DomainException($"A category named '{name}' already exists.");

        ItemCategory category;
        if (cmd.CategoryId is { } id)
        {
            category = await db.ItemCategories.FindAsync([new CategoryId(id)], ct)
                ?? throw new NotFoundException($"Category {id} not found.");
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
