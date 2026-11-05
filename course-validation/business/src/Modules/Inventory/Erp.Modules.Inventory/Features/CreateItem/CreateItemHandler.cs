using Erp.Modules.Inventory.Domain;
using Erp.Modules.Inventory.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Inventory.Features;

internal sealed record CreateItemCommand(string Sku, string Name, string Unit, bool AllowsFractions) : ICommand<long>;

internal sealed class CreateItemValidator : AbstractValidator<CreateItemCommand>
{
    public CreateItemValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Unit).NotEmpty().MaximumLength(10);
    }
}

internal sealed class CreateItemHandler(InventoryDbContext db) : ICommandHandler<CreateItemCommand, long>
{
    public async ValueTask<long> Handle(CreateItemCommand cmd, CancellationToken ct)
    {
        var sku = cmd.Sku.Trim().ToUpperInvariant();
        if (await db.Items.AnyAsync(i => i.Sku == sku, ct))
            throw new DomainException($"An item with SKU {sku} already exists.");

        var id = new ItemId(await db.NextItemIdAsync(ct));
        db.Items.Add(Item.Create(id, sku, cmd.Name, cmd.Unit, cmd.AllowsFractions));
        await db.SaveChangesAsync(ct);
        return id.Value;
    }
}
