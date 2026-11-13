using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

// Sales rules that apply to a group of items: who may sell them, and how much per order.
internal sealed class ItemCategory : Entity<CategoryId>
{
    public string Name { get; private set; } = default!;
    public decimal MaxQuantityPerOrder { get; private set; }
    public string? RequiredRole { get; private set; }   // null = anyone who may sell

    private ItemCategory() { }

    public static ItemCategory Create(string name, decimal maxQuantityPerOrder, string? requiredRole)
    {
        var category = new ItemCategory();
        category.Update(name, maxQuantityPerOrder, requiredRole);
        return category;
    }

    public void Update(string name, decimal maxQuantityPerOrder, string? requiredRole)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException(SalesErrors.CategoryNameRequired, "A category needs a name.");
        if (maxQuantityPerOrder <= 0) throw new DomainException(SalesErrors.CategoryMaxQuantityInvalid, "The maximum quantity per order must be greater than zero.");
        if (requiredRole is not null && !Roles.All.Contains(requiredRole))
            throw new DomainException(SalesErrors.UnknownRole, $"'{requiredRole}' is not a role.");
        Name = name.Trim();
        MaxQuantityPerOrder = maxQuantityPerOrder;
        RequiredRole = requiredRole;
    }

    public void EnsureRoleAllowed(string callerRole)
    {
        if (RequiredRole is not null && callerRole != RequiredRole && callerRole != Roles.Admin)
            throw new DomainException(SalesErrors.CategoryRoleRequired, $"Category '{Name}' requires role '{RequiredRole}'.");
    }

    public void EnsureQuantityAllowed(decimal quantityOnOrder)
    {
        if (quantityOnOrder > MaxQuantityPerOrder)
            throw new DomainException(SalesErrors.CategoryQuantityExceeded,
                $"Category '{Name}' allows at most {MaxQuantityPerOrder:0.###} per order (requested {quantityOnOrder:0.###}).");
    }
}
