using Erp.Modules.Purchasing.Domain;
using Erp.Modules.Purchasing.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;

namespace Erp.Modules.Purchasing.Features;

internal sealed record SupplierInput(string Name, string? Tin, string? Address, string? Email, string? Phone, int PaymentTermsDays);

// One command for create (no id) and edit (id).
internal sealed record SaveSupplierCommand(long? SupplierId, SupplierInput Supplier) : ICommand<long>;

internal sealed class SaveSupplierValidator : AbstractValidator<SaveSupplierCommand>
{
    public SaveSupplierValidator()
    {
        RuleFor(x => x.Supplier.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Supplier.Tin).MaximumLength(20);
        RuleFor(x => x.Supplier.Address).MaximumLength(500);
        RuleFor(x => x.Supplier.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Supplier.Email));
        RuleFor(x => x.Supplier.Phone).MaximumLength(50);
        RuleFor(x => x.Supplier.PaymentTermsDays).InclusiveBetween(0, 365);
    }
}

internal sealed class SaveSupplierHandler(PurchasingDbContext db) : ICommandHandler<SaveSupplierCommand, long>
{
    public async ValueTask<long> Handle(SaveSupplierCommand cmd, CancellationToken ct)
    {
        var s = cmd.Supplier;
        Supplier supplier;
        if (cmd.SupplierId is { } id)
        {
            supplier = await db.Suppliers.FindAsync([new SupplierId(id)], ct) ?? throw new NotFoundException($"Supplier {id} not found.");
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

internal sealed record SetSupplierActiveCommand(long SupplierId, bool Active) : ICommand;

internal sealed class SetSupplierActiveHandler(PurchasingDbContext db) : ICommandHandler<SetSupplierActiveCommand>
{
    public async ValueTask<Unit> Handle(SetSupplierActiveCommand cmd, CancellationToken ct)
    {
        var supplier = await db.Suppliers.FindAsync([new SupplierId(cmd.SupplierId)], ct)
            ?? throw new NotFoundException($"Supplier {cmd.SupplierId} not found.");
        supplier.SetActive(cmd.Active);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
