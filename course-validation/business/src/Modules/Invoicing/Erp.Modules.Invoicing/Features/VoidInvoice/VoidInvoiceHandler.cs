using Erp.Modules.Invoicing.Domain;
using Erp.Modules.Invoicing.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Invoicing.Features;

internal sealed record VoidInvoiceCommand(long InvoiceId, string Reason);

internal sealed class VoidInvoiceHandler(InvoicingDbContext db, ICurrentUser user, TimeProvider time)
{
    public async Task<bool> HandleAsync(VoidInvoiceCommand cmd, CancellationToken ct)
    {
        var invoice = await db.Invoices.SingleOrDefaultAsync(i => i.Id == new InvoiceId(cmd.InvoiceId), ct);
        if (invoice is null) return false;
        invoice.Void(cmd.Reason, new UserId(user.UserId), time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return true;
    }
}

// After voiding a wrong invoice, bill the same shipment again under a new number.
internal sealed record ReissueInvoiceCommand(long VoidInvoiceId);

internal sealed class ReissueInvoiceHandler(InvoicingDbContext db, InvoiceIssuer issuer)
{
    // null: there is no such invoice.
    public async Task<long?> HandleAsync(ReissueInvoiceCommand cmd, CancellationToken ct)
    {
        var voided = await db.Invoices.AsNoTracking().SingleOrDefaultAsync(i => i.Id == new InvoiceId(cmd.VoidInvoiceId), ct);
        if (voided is null) return null;
        voided.EnsureCanBeReissued();
        return await issuer.IssueForShipmentAsync(voided.ShipmentId, voided.SalesOrderId, ct);
    }
}
