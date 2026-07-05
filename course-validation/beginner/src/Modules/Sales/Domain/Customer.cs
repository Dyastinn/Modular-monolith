using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Domain;

// NOTE: Customer + EnsureCanOrder are referenced by the handler in Phase 4 but never shown. Inferred.
public sealed class Customer : Entity<CustomerId>
{
    public string Name { get; private set; } = "";
    public bool IsActive { get; private set; }
    public bool OnCreditHold { get; private set; }

    private Customer() { }  // for repository rehydration

    public void EnsureCanOrder()
    {
        if (!IsActive)
            throw new DomainException("Customer is inactive and cannot place orders.");
        if (OnCreditHold)
            throw new DomainException("Customer is on credit hold and cannot place orders.");
    }

    // Rehydrate from DB rows (repository uses the private constructor).
    public static Customer Rehydrate(CustomerId id, string name, bool isActive, bool onCreditHold)
        => new() { Id = id, Name = name, IsActive = isActive, OnCreditHold = onCreditHold };
}
