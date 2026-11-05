using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

internal sealed class ItemCategory
{
    public long Id { get; private set; }
    public string Name { get; private set; } = default!;
    public int MaxQuantityPerOrder { get; private set; }
    public string? RequiredRole { get; private set; }   // null = anyone

    private ItemCategory() { }   // for EF

    public ItemCategory(long id, string name, int maxQuantityPerOrder, string? requiredRole)
    { Id = id; Name = name; MaxQuantityPerOrder = maxQuantityPerOrder; RequiredRole = requiredRole; }

    public void EnsureRoleAllowed(string callerRole)
    {
        if (RequiredRole is not null && !string.Equals(callerRole, RequiredRole, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(callerRole, Roles.Admin, StringComparison.OrdinalIgnoreCase))
            throw new DomainException($"Category '{Name}' requires role '{RequiredRole}'.");
    }

    public void EnsureQuantityAllowed(int quantityOnOrder)
    {
        if (quantityOnOrder > MaxQuantityPerOrder)
            throw new DomainException(
                $"Category '{Name}' allows at most {MaxQuantityPerOrder} units per order (requested {quantityOnOrder}).");
    }
}
