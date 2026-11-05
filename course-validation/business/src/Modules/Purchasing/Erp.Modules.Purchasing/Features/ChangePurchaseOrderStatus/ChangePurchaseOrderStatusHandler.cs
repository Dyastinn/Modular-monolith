using Erp.Modules.Purchasing.Domain;
using Erp.Modules.Purchasing.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Purchasing.Features;

internal enum PurchaseOrderAction { Approve, Send, Close, Cancel }

// The status changes that touch nothing but the order itself.
internal sealed record ChangePurchaseOrderStatusCommand(long PurchaseOrderId, PurchaseOrderAction Action) : ICommand;

internal sealed class ChangePurchaseOrderStatusHandler(PurchasingDbContext db, ICurrentUser user, IClock clock)
    : ICommandHandler<ChangePurchaseOrderStatusCommand>
{
    public async ValueTask<Unit> Handle(ChangePurchaseOrderStatusCommand cmd, CancellationToken ct)
    {
        var po = await db.PurchaseOrders.SingleOrDefaultAsync(p => p.Id == new PurchaseOrderId(cmd.PurchaseOrderId), ct)
            ?? throw new NotFoundException($"Purchase order {cmd.PurchaseOrderId} not found.");

        switch (cmd.Action)
        {
            case PurchaseOrderAction.Approve: po.Approve(new UserId(user.UserId), clock.UtcNow); break;
            case PurchaseOrderAction.Send: po.MarkSent(clock.UtcNow); break;
            case PurchaseOrderAction.Close: po.Close(); break;
            case PurchaseOrderAction.Cancel: po.Cancel(); break;
        }

        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
