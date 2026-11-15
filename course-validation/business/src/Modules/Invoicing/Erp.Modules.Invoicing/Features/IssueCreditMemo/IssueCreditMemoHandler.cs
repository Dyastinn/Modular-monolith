using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Invoicing.Domain;
using Erp.Modules.Invoicing.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Erp.Modules.Invoicing.Features;

internal sealed record IssueCreditMemoCommand(long InvoiceId, IReadOnlyList<(long LineId, decimal Quantity)> Lines, string Reason, bool ReturnToStock);

// The memo, the invoice it credits and the goods back on the shelf: one transaction across two modules.
internal sealed class IssueCreditMemoHandler(InvoicingDbContext db, IStockReturns stock, ICurrentUser user, TimeProvider time)
{
    // null: there is no such invoice.
    public async Task<long?> HandleAsync(IssueCreditMemoCommand cmd, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var invoice = await db.Invoices.SingleOrDefaultAsync(i => i.Id == new InvoiceId(cmd.InvoiceId), ct);
        if (invoice is null) return null;

        var nowUtc = time.GetUtcNow().UtcDateTime;
        // Taken inside the transaction: a refused credit rolls the counter back, so no number is skipped.
        var number = await db.AllocateCreditMemoNumberAsync(nowUtc, ct);
        var memo = invoice.Credit(number, cmd.Lines, cmd.Reason, cmd.ReturnToStock, new UserId(user.UserId), time.Today(), nowUtc);
        db.CreditMemos.Add(memo);
        await db.SaveChangesAsync(ct);   // xmin: a payment recorded meanwhile makes this a 409, not a double credit

        foreach (var (itemId, quantity, unitCost) in memo.StockToReturn())
            await stock.ReturnAsync(itemId, quantity, unitCost, number, user.UserId, tx.GetDbTransaction(), ct);
        await tx.CommitAsync(ct);
        return memo.Id.Value;
    }
}

internal sealed record RecordRefundCommand(long CreditMemoId, DateOnly PaidOn, PaymentMethod Method, string? Reference);

internal sealed class RecordRefundHandler(InvoicingDbContext db, TimeProvider time)
{
    public async Task<bool> HandleAsync(RecordRefundCommand cmd, CancellationToken ct)
    {
        var memo = await db.CreditMemos.SingleOrDefaultAsync(m => m.Id == new CreditMemoId(cmd.CreditMemoId), ct);
        if (memo is null) return false;
        memo.RecordRefund(cmd.PaidOn, cmd.Method, cmd.Reference, time.Today());
        await db.SaveChangesAsync(ct);
        return true;
    }
}
