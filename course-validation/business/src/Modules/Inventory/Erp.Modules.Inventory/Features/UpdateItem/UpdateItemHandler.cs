using Erp.Modules.Inventory.Domain;
using Erp.Modules.Inventory.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Inventory.Features;

internal sealed record UpdateItemCommand(long ItemId, string Name, string Unit, bool AllowsFractions) : ICommand;

internal sealed class UpdateItemValidator : AbstractValidator<UpdateItemCommand>
{
    public UpdateItemValidator()
    {
        RuleFor(x => x.ItemId).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Unit).NotEmpty().MaximumLength(10);
    }
}

internal sealed class UpdateItemHandler(InventoryDbContext db) : ICommandHandler<UpdateItemCommand>
{
    public async ValueTask<Unit> Handle(UpdateItemCommand cmd, CancellationToken ct)
    {
        var item = await db.Items.FindAsync([new ItemId(cmd.ItemId)], ct)
            ?? throw new NotFoundException($"Item {cmd.ItemId} not found.");
        item.Describe(cmd.Name, cmd.Unit, cmd.AllowsFractions);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
