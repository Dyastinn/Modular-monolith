using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Purchasing.Domain;
using Erp.Modules.Purchasing.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Erp.Modules.Purchasing.Features;

internal sealed record ReceivedLineInput(long LineId, decimal Quantity);

internal sealed record ReceivePurchaseOrderCommand(long PurchaseOrderId, IReadOnlyList<ReceivedLineInput> Lines) : ICommand;

internal sealed class ReceivePurchaseOrderValidator : AbstractValidator<ReceivePurchaseOrderCommand>
{
    public ReceivePurchaseOrderValidator()
    {
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Enter at least one received quantity.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.LineId).GreaterThan(0);
            l.RuleFor(x => x.Quantity).ValidQuantity();
        });
    }
}

// Goods in: the order records what arrived and Inventory's stock goes up, in one transaction.
internal sealed class ReceivePurchaseOrderHandler(PurchasingDbContext db, IStockReceiving stock, ICurrentUser user)
    : ICommandHandler<ReceivePurchaseOrderCommand>
{
    public async ValueTask<Unit> Handle(ReceivePurchaseOrderCommand cmd, CancellationToken ct)
    {
        var po = await db.PurchaseOrders.SingleOrDefaultAsync(p => p.Id == new PurchaseOrderId(cmd.PurchaseOrderId), ct)
            ?? throw new NotFoundException($"Purchase order {cmd.PurchaseOrderId} not found.");

        var toStock = po.Receive(cmd.Lines.Select(l => (l.LineId, new Quantity(l.Quantity))).ToList());

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var (itemId, quantity) in toStock)
            await stock.ReceiveAsync(itemId.Value, quantity.Value, po.Number, user.UserId, tx.GetDbTransaction(), ct);
        await db.SaveChangesAsync(ct);   // xmin: two clerks receiving the same order at once can't both count
        await tx.CommitAsync(ct);
        return Unit.Value;
    }
}
