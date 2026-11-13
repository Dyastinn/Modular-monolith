using Erp.Modules.Purchasing.Domain;
using Erp.Modules.Purchasing.Infrastructure;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Purchasing.Features;

internal sealed record SupplierInput(string Name, string? Tin, string? Address, string? Email, string? Phone, int PaymentTermsDays);

// One command for create (no id) and edit (id).
internal sealed record SaveSupplierCommand(long? SupplierId, SupplierInput Supplier);

internal sealed class SaveSupplierHandler(PurchasingDbContext db)
{
    // null: the supplier to update doesn't exist.
    public async Task<long?> HandleAsync(SaveSupplierCommand cmd, CancellationToken ct)
    {
        var s = cmd.Supplier;
        Supplier supplier;
        if (cmd.SupplierId is { } id)
        {
            var existing = await db.Suppliers.FindAsync([new SupplierId(id)], ct);
            if (existing is null) return null;
            supplier = existing;
            supplier.Update(s.Name, s.Tin, s.Address, s.Email, s.Phone, s.PaymentTermsDays);
        }
        else
        {
            supplier = Supplier.Create(s.Name, s.Tin, s.Address, s.Email, s.Phone, s.PaymentTermsDays);
            db.Suppliers.Add(supplier);
        }
        await db.SaveChangesAsync(ct);
        return supplier.Id.Value;
    }
}

internal sealed record SetSupplierActiveCommand(long SupplierId, bool Active);

internal sealed class SetSupplierActiveHandler(PurchasingDbContext db)
{
    public async Task<bool> HandleAsync(SetSupplierActiveCommand cmd, CancellationToken ct)
    {
        var supplier = await db.Suppliers.FindAsync([new SupplierId(cmd.SupplierId)], ct);
        if (supplier is null) return false;
        supplier.SetActive(cmd.Active);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
