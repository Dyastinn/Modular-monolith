using Erp.Modules.Inventory.Domain;
using Erp.Modules.Inventory.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Inventory.Features;

internal sealed record SetItemActiveCommand(long ItemId, bool Active) : ICommand;

internal sealed class SetItemActiveHandler(InventoryDbContext db) : ICommandHandler<SetItemActiveCommand>
{
    public async ValueTask<Unit> Handle(SetItemActiveCommand cmd, CancellationToken ct)
    {
        var item = await db.Items.FindAsync([new ItemId(cmd.ItemId)], ct)
            ?? throw new NotFoundException($"Item {cmd.ItemId} not found.");
        item.SetActive(cmd.Active);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
