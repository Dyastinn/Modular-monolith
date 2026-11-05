using Erp.Modules.Inventory.Domain;
using Erp.Modules.Inventory.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Inventory.Features;

internal sealed record AdjustStockCommand(long ItemId, decimal Quantity, string Reason) : ICommand;

internal sealed class AdjustStockValidator : AbstractValidator<AdjustStockCommand>
{
    public AdjustStockValidator()
    {
        RuleFor(x => x.ItemId).GreaterThan(0);
        RuleFor(x => x.Quantity).NotEqual(0).Must(q => decimal.Round(q, 3) == q)
            .WithMessage("Quantity must be non-zero, with at most three decimal places.");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

internal sealed class AdjustStockHandler(InventoryDbContext db, ICurrentUser user, IClock clock) : ICommandHandler<AdjustStockCommand>
{
    public async ValueTask<Unit> Handle(AdjustStockCommand cmd, CancellationToken ct)
    {
        var item = await db.Items.FindAsync([new ItemId(cmd.ItemId)], ct)
            ?? throw new NotFoundException($"Item {cmd.ItemId} not found.");
        db.StockMovements.Add(item.Adjust(cmd.Quantity, cmd.Reason, new UserId(user.UserId), clock.UtcNow));
        await db.SaveChangesAsync(ct);   // xmin: a reservation that landed meanwhile makes this a 409
        return Unit.Value;
    }
}
