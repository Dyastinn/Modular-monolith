using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Purchasing.Domain;

internal readonly record struct SupplierId(long Value);
internal readonly record struct PurchaseOrderId(long Value);
internal readonly record struct ItemId(long Value);   // owned by Inventory
internal readonly record struct UserId(long Value);   // owned by Identity

internal sealed class Supplier : Entity<SupplierId>
{
    public string Name { get; private set; } = default!;
    public string? Tin { get; private set; }
    public string? Address { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public int PaymentTermsDays { get; private set; }
    public bool IsActive { get; private set; }

    private Supplier() { }

    public static Supplier Create(string name, string? tin, string? address, string? email, string? phone, int paymentTermsDays)
    {
        var supplier = new Supplier { IsActive = true };
        supplier.Update(name, tin, address, email, phone, paymentTermsDays);
        return supplier;
    }

    public void Update(string name, string? tin, string? address, string? email, string? phone, int paymentTermsDays)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("A supplier needs a name.");
        if (paymentTermsDays is < 0 or > 365) throw new DomainException("Payment terms must be between 0 and 365 days.");
        Name = name.Trim();
        Tin = Clean(tin); Address = Clean(address); Email = Clean(email); Phone = Clean(phone);
        PaymentTermsDays = paymentTermsDays;
    }

    public void SetActive(bool active) => IsActive = active;

    public void EnsureCanOrderFrom()
    {
        if (!IsActive) throw new DomainException($"Supplier '{Name}' is inactive.");
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
