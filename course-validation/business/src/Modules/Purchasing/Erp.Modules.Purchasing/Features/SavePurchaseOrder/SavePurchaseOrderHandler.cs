using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Purchasing.Domain;
using Erp.Modules.Purchasing.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Purchasing.Features;

internal sealed record PurchaseLineInput(long ItemId, decimal Quantity, decimal UnitCost);

// One command for a new draft (no id) and for editing a draft (id).
internal sealed record SavePurchaseOrderCommand(
    long? PurchaseOrderId, long SupplierId, DateOnly? ExpectedDate, string? Notes, IReadOnlyList<PurchaseLineInput> Lines)
    : ICommand<long>;

internal sealed class SavePurchaseOrderValidator : AbstractValidator<SavePurchaseOrderCommand>
{
    public SavePurchaseOrderValidator()
    {
        RuleFor(x => x.SupplierId).GreaterThan(0);
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("A purchase order needs at least one line.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.ItemId).GreaterThan(0);
            l.RuleFor(x => x.Quantity).ValidQuantity();
            l.RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0);
        });
    }
}

internal sealed class SavePurchaseOrderHandler(PurchasingDbContext db, IItemCatalog catalog, ICurrentUser user, IClock clock)
    : ICommandHandler<SavePurchaseOrderCommand, long>
{
    public async ValueTask<long> Handle(SavePurchaseOrderCommand cmd, CancellationToken ct)
    {
        var supplier = await db.Suppliers.FindAsync([new SupplierId(cmd.SupplierId)], ct)
            ?? throw new NotFoundException($"Supplier {cmd.SupplierId} not found.");

        PurchaseOrder po;
        if (cmd.PurchaseOrderId is { } id)
        {
            po = await db.PurchaseOrders.SingleOrDefaultAsync(p => p.Id == new PurchaseOrderId(id), ct)
                ?? throw new NotFoundException($"Purchase order {id} not found.");
        }
        else
        {
            var now = clock.UtcNow;
            po = PurchaseOrder.Create(await db.NextOrderNumberAsync(now, ct), supplier, new UserId(user.UserId), now);
            db.PurchaseOrders.Add(po);
        }

        var items = (await catalog.GetAsync(cmd.Lines.Select(l => l.ItemId).Distinct().ToList(), ct)).ToDictionary(i => i.Id);
        var lines = cmd.Lines.Select(l => items.TryGetValue(l.ItemId, out var i)
                ? new OrderLineSpec(new ItemId(i.Id), i.Sku, i.Name, i.Unit, i.AllowsFractions, i.IsActive,
                    new Quantity(l.Quantity), new Money(l.UnitCost))
                : throw new NotFoundException($"Item {l.ItemId} not found."))
            .ToList();

        po.Describe(cmd.ExpectedDate, cmd.Notes);
        po.SetLines(lines);
        await db.SaveChangesAsync(ct);
        return po.Id.Value;
    }
}
