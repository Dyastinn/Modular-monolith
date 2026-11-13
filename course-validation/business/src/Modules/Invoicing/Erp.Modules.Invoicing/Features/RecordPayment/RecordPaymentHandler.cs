using Erp.Modules.Invoicing.Domain;
using Erp.Modules.Invoicing.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Invoicing.Features;

internal sealed record RecordPaymentCommand(long InvoiceId, decimal Amount, decimal TaxWithheld, DateOnly PaidOn, PaymentMethod Method,
    string? Reference);

// Saving the payment also writes PaymentRecorded to the outbox; Sales lowers the customer's balance from it.
internal sealed class RecordPaymentHandler(InvoicingDbContext db, ICurrentUser user, TimeProvider time)
{
    // null: there is no such invoice.
    public async Task<long?> HandleAsync(RecordPaymentCommand cmd, CancellationToken ct)
    {
        var invoice = await db.Invoices.SingleOrDefaultAsync(i => i.Id == new InvoiceId(cmd.InvoiceId), ct);
        if (invoice is null) return null;

        var paymentId = await db.NextPaymentIdAsync(ct);
        invoice.RecordPayment(paymentId, new Money(cmd.Amount), new Money(cmd.TaxWithheld), cmd.PaidOn, cmd.Method, cmd.Reference, new UserId(user.UserId),
            time.Today(), time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return paymentId.Value;
    }
}

internal sealed record VoidPaymentCommand(long InvoiceId, long PaymentId, string Reason);

internal sealed class VoidPaymentHandler(InvoicingDbContext db, TimeProvider time)
{
    public async Task<bool> HandleAsync(VoidPaymentCommand cmd, CancellationToken ct)
    {
        var invoice = await db.Invoices.SingleOrDefaultAsync(i => i.Id == new InvoiceId(cmd.InvoiceId), ct);
        if (invoice is null) return false;
        invoice.VoidPayment(new PaymentId(cmd.PaymentId), cmd.Reason, time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
