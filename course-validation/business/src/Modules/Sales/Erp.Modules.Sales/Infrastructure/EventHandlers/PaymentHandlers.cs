using Erp.Modules.Invoicing.Contracts;
using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Sales.Infrastructure;

// Money in lowers what the customer owes, so their next order has room again. The applied_payment row makes a
// second delivery of the same event a no-op.
internal sealed class PaymentRecordedHandler(SalesDbContext db, TimeProvider time) : IIntegrationEventHandler<PaymentRecorded>
{
    public async Task HandleAsync(PaymentRecorded e, CancellationToken ct)
    {
        if (await db.AppliedPayments.AnyAsync(p => p.PaymentId == e.PaymentId, ct)) return;

        var customer = await db.Customers.SingleAsync(c => c.Id == new CustomerId(e.CustomerId), ct);
        db.AppliedPayments.Add(AppliedPayment.Apply(e.PaymentId, customer, new Money(e.Amount), time.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);   // a racing duplicate fails on the primary key, and the outbox retries it
    }
}

internal sealed class PaymentVoidedHandler(SalesDbContext db, TimeProvider time) : IIntegrationEventHandler<PaymentVoided>
{
    public async Task HandleAsync(PaymentVoided e, CancellationToken ct)
    {
        var applied = await db.AppliedPayments.SingleOrDefaultAsync(p => p.PaymentId == e.PaymentId, ct);
        if (applied is null) return;   // never applied, so nothing to undo

        var customer = await db.Customers.SingleAsync(c => c.Id == applied.CustomerId, ct);
        applied.Reverse(customer, time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
    }
}
