using Erp.Modules.Invoicing.Contracts;
using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Sales.Infrastructure;

// Money in lowers what the customer owes, so their next order has room again. The applied_payment row makes a
// second delivery of the same event a no-op.
internal sealed class PaymentRecordedHandler(SalesDbContext db, IClock clock) : INotificationHandler<PaymentRecorded>
{
    public async ValueTask Handle(PaymentRecorded e, CancellationToken ct)
    {
        if (await db.AppliedPayments.AnyAsync(p => p.PaymentId == e.PaymentId, ct)) return;

        var customer = await db.Customers.SingleAsync(c => c.Id == new CustomerId(e.CustomerId), ct);
        db.AppliedPayments.Add(AppliedPayment.Apply(e.PaymentId, customer, new Money(e.Amount), clock.UtcNow));
        await db.SaveChangesAsync(ct);   // a racing duplicate fails on the primary key, and the outbox retries it
    }
}

internal sealed class PaymentVoidedHandler(SalesDbContext db, IClock clock) : INotificationHandler<PaymentVoided>
{
    public async ValueTask Handle(PaymentVoided e, CancellationToken ct)
    {
        var applied = await db.AppliedPayments.SingleOrDefaultAsync(p => p.PaymentId == e.PaymentId, ct);
        if (applied is null) return;   // never applied, so nothing to undo

        var customer = await db.Customers.SingleAsync(c => c.Id == applied.CustomerId, ct);
        applied.Reverse(customer, clock.UtcNow);
        await db.SaveChangesAsync(ct);
    }
}
