using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Purchasing.Domain;
using Erp.Modules.Purchasing.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Purchasing.Features;

internal sealed record PurchaseLineInput(long ItemId, decimal Quantity, decimal UnitCost);

// One command for a new draft (no id) and for editing a draft (id).
internal sealed record SavePurchaseOrderCommand(
    long? PurchaseOrderId, long SupplierId, DateOnly? ExpectedDate, string? Notes, IReadOnlyList<PurchaseLineInput> Lines);

internal sealed class SavePurchaseOrderHandler(PurchasingDbContext db, IItemCatalog catalog, ICurrentUser user, TimeProvider time)
{
    // null: the purchase order to update doesn't exist.
    public async Task<long?> HandleAsync(SavePurchaseOrderCommand cmd, CancellationToken ct)
    {
        var supplier = await db.Suppliers.FindAsync([new SupplierId(cmd.SupplierId)], ct)
            ?? throw new DomainException(PurchasingErrors.SupplierNotFound, $"Supplier {cmd.SupplierId} doesn't exist.");

        PurchaseOrder po;
        if (cmd.PurchaseOrderId is { } id)
        {
            var existing = await db.PurchaseOrders.SingleOrDefaultAsync(p => p.Id == new PurchaseOrderId(id), ct);
            if (existing is null) return null;
            po = existing;
        }
        else
        {
            var now = time.GetUtcNow().UtcDateTime;
            po = PurchaseOrder.Create(await db.NextOrderNumberAsync(now, ct), supplier, new UserId(user.UserId), now);
            db.PurchaseOrders.Add(po);
        }

        var items = (await catalog.GetAsync(cmd.Lines.Select(l => l.ItemId).Distinct().ToList(), ct)).ToDictionary(i => i.Id);
        var lines = cmd.Lines.Select(l => items.TryGetValue(l.ItemId, out var i)
                ? new OrderLineSpec(new ItemId(i.Id), i.Sku, i.Name, i.Unit, i.AllowsFractions, i.IsActive,
                    new Quantity(l.Quantity), new Money(l.UnitCost))
                : throw new DomainException(PurchasingErrors.ItemNotFound, $"Item {l.ItemId} doesn't exist."))
            .ToList();

        po.Describe(cmd.ExpectedDate, cmd.Notes);
        po.SetLines(lines);
        await db.SaveChangesAsync(ct);
        return po.Id.Value;
    }
}
