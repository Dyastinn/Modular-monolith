using Erp.Modules.Invoicing.Contracts;
using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Sales.Infrastructure;

// An old debt counts against credit like an approved order does.
internal sealed class OpeningBalanceChargedHandler(SalesDbContext db, TimeProvider time) : IIntegrationEventHandler<OpeningBalanceCharged>
{
    public async Task HandleAsync(OpeningBalanceCharged e, CancellationToken ct)
    {
        if (await db.AppliedCharges.AnyAsync(c => c.DocumentNumber == e.Number, ct)) return;

        var customer = await db.Customers.SingleAsync(c => c.Id == new CustomerId(e.CustomerId), ct);
        db.AppliedCharges.Add(AppliedCharge.Apply(e.Number, customer, new Money(e.Amount), time.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);   // a racing duplicate fails on the primary key, and the outbox retries it
    }
}

internal sealed class OpeningBalanceVoidedHandler(SalesDbContext db, TimeProvider time) : IIntegrationEventHandler<OpeningBalanceVoided>
{
    public async Task HandleAsync(OpeningBalanceVoided e, CancellationToken ct)
    {
        var applied = await db.AppliedCharges.SingleOrDefaultAsync(c => c.DocumentNumber == e.Number, ct);
        if (applied is null) return;   // never applied, so nothing to undo

        var customer = await db.Customers.SingleAsync(c => c.Id == applied.CustomerId, ct);
        applied.Reverse(customer, time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
    }
}

// Goods came back or a price was wrong: the customer owes less, and has room for the next order again.
internal sealed class CreditMemoIssuedHandler(SalesDbContext db, TimeProvider time) : IIntegrationEventHandler<CreditMemoIssued>
{
    public async Task HandleAsync(CreditMemoIssued e, CancellationToken ct)
    {
        if (e.AppliedToInvoice == 0 || await db.AppliedCredits.AnyAsync(c => c.DocumentNumber == e.Number, ct)) return;

        var customer = await db.Customers.SingleAsync(c => c.Id == new CustomerId(e.CustomerId), ct);
        db.AppliedCredits.Add(AppliedCredit.Apply(e.Number, customer, new Money(e.AppliedToInvoice), time.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
    }
}
