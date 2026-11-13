using Erp.Modules.Invoicing.Domain;
using Erp.Modules.Sales.Contracts;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Invoicing.Infrastructure;

// Bills one shipment. Safe to call again: if the shipment already has a live invoice, that one is returned.
internal sealed class InvoiceIssuer(InvoicingDbContext db, ISalesOrders orders, TimeProvider time)
{
    public async Task<long> IssueForShipmentAsync(long shipmentId, long salesOrderId, CancellationToken ct)
    {
        if (await LiveInvoiceForAsync(shipmentId, ct) is { } existing) return existing;

        var order = await orders.GetForInvoiceAsync(salesOrderId, ct);
        var company = await db.CompanyProfileAsync(ct);
        company.EnsureReadyToInvoice();
        var lines = order.Lines
            .Select(l => new InvoiceLine(l.Description, l.Unit, new Quantity(l.Quantity), new Money(l.UnitPrice), l.TaxType,
                l.SpecialDiscountEligible))
            .ToList();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Taking the next number locks the counter, so two deliveries of the same event are now in single file.
        var number = await db.AllocateInvoiceNumberAsync(time.GetUtcNow().UtcDateTime, ct);
        if (await LiveInvoiceForAsync(shipmentId, ct) is { } raced) return raced;   // rollback: the number isn't used up

        var invoice = Invoice.Issue(number, company.Seller(), shipmentId, order.OrderId, order.OrderNumber, order.CustomerId, order.CustomerName,
            order.CustomerTin, order.CustomerAddress, order.PaymentTermsDays, lines, time.Today(), company.Tax,
            order.SpecialDiscount);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return invoice.Id.Value;
    }

    private async Task<long?> LiveInvoiceForAsync(long shipmentId, CancellationToken ct)
    {
        var id = await db.Invoices.Where(i => i.ShipmentId == shipmentId && i.Status != InvoiceStatus.Void)
            .Select(i => i.Id).FirstOrDefaultAsync(ct);
        return id == default ? null : id.Value;
    }
}
