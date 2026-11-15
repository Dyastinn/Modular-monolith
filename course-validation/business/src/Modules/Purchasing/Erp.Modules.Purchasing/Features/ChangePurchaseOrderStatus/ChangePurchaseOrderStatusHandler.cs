using Erp.Modules.Purchasing.Domain;
using Erp.Modules.Purchasing.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Purchasing.Features;

internal enum PurchaseOrderAction { Submit, Approve, Send, Close, Cancel }

// The status changes that touch nothing but the order itself.
internal sealed record ChangePurchaseOrderStatusCommand(long PurchaseOrderId, PurchaseOrderAction Action);

internal sealed class ChangePurchaseOrderStatusHandler(PurchasingDbContext db, ICurrentUser user, TimeProvider time)
{
    public async Task<bool> HandleAsync(ChangePurchaseOrderStatusCommand cmd, CancellationToken ct)
    {
        var po = await db.PurchaseOrders.SingleOrDefaultAsync(p => p.Id == new PurchaseOrderId(cmd.PurchaseOrderId), ct);
        if (po is null) return false;

        switch (cmd.Action)
        {
            case PurchaseOrderAction.Submit: po.Submit(await db.SettingsAsync(ct), time.GetUtcNow().UtcDateTime); break;
            case PurchaseOrderAction.Approve: po.Approve(new UserId(user.UserId), time.GetUtcNow().UtcDateTime); break;
            case PurchaseOrderAction.Send: po.MarkSent(time.GetUtcNow().UtcDateTime); break;
            case PurchaseOrderAction.Close: po.Close(); break;
            case PurchaseOrderAction.Cancel: po.Cancel(); break;
        }

        await db.SaveChangesAsync(ct);
        return true;
    }
}

internal sealed record RejectPurchaseOrderCommand(long PurchaseOrderId, string Reason);

internal sealed class RejectPurchaseOrderHandler(PurchasingDbContext db, ICurrentUser user, TimeProvider time)
{
    public async Task<bool> HandleAsync(RejectPurchaseOrderCommand cmd, CancellationToken ct)
    {
        var po = await db.PurchaseOrders.SingleOrDefaultAsync(p => p.Id == new PurchaseOrderId(cmd.PurchaseOrderId), ct);
        if (po is null) return false;
        po.Reject(new UserId(user.UserId), cmd.Reason, time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
