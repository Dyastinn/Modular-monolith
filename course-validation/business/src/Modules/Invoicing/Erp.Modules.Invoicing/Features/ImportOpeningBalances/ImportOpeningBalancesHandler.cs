using Erp.Modules.Invoicing.Domain;
using Erp.Modules.Invoicing.Infrastructure;
using Erp.Modules.Sales.Contracts;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Invoicing.Features;

// One row: what a customer (by name) owed on one old invoice on the day the system went live.
// DueDate left out: the invoice date plus the customer's payment terms.
internal sealed record OpeningBalanceRow(string Customer, string Reference, DateOnly InvoiceDate, decimal Amount, DateOnly? DueDate = null);

internal sealed record ImportOpeningBalancesCommand(IReadOnlyList<OpeningBalanceRow> Rows, bool Commit);

internal sealed class ImportOpeningBalancesHandler(InvoicingDbContext db, ICustomerDirectory customers, TimeProvider time)
{
    public async Task<ImportResult> HandleAsync(ImportOpeningBalancesCommand cmd, CancellationToken ct)
    {
        var found = (await customers.FindByNamesAsync(cmd.Rows.Select(r => r.Customer).ToList(), ct))
            .ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        // Customer + old invoice number identifies one debt: in the file, and among what was imported before.
        var recorded = (await db.Invoices.Where(i => i.IsOpeningBalance && i.Status != InvoiceStatus.Void)
                .Select(i => new { i.CustomerId, i.OrderNumber }).ToListAsync(ct))
            .Select(i => $"{i.CustomerId}|{i.OrderNumber.ToUpperInvariant()}").ToHashSet();
        var seller = (await db.CompanyProfileAsync(ct)).Seller();
        var today = time.Today();
        var nowUtc = time.GetUtcNow().UtcDateTime;
        var errors = new List<ImportError>();
        var built = new List<(OpeningBalanceRow Row, CustomerSummary Customer)>();

        for (var i = 0; i < cmd.Rows.Count; i++)
        {
            var r = cmd.Rows[i];
            try
            {
                var customer = found.GetValueOrDefault(r.Customer.Trim())
                    ?? throw new DomainException(InvoicingErrors.CustomerNotFound, $"There is no customer named '{r.Customer.Trim()}'.");
                if (!recorded.Add($"{customer.Id}|{r.Reference.Trim().ToUpperInvariant()}"))
                    throw new DomainException(InvoicingErrors.DuplicateOpeningBalance, $"{customer.Name}'s {r.Reference.Trim()} is already recorded.");
                Build("OB-check", seller, customer, r, today);   // every rule, with no number used up
                built.Add((r, customer));
            }
            catch (DomainException e)
            {
                errors.Add(new ImportError(i + 1, e.Message, e.Error.Code));
            }
        }

        if (errors.Count > 0 || !cmd.Commit) return new ImportResult(cmd.Rows.Count, 0, errors);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var (row, customer) in built)
            db.Invoices.Add(Build(await db.AllocateOpeningBalanceNumberAsync(nowUtc, ct), seller, customer, row, today));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new ImportResult(cmd.Rows.Count, built.Count, []);
    }

    private static Invoice Build(string number, SellerDetails seller, CustomerSummary c, OpeningBalanceRow r, DateOnly today) =>
        Invoice.OpeningBalance(number, seller, c.Id, c.Name, c.Tin, c.Address, r.Reference, r.InvoiceDate,
            r.DueDate ?? r.InvoiceDate.AddDays(c.PaymentTermsDays), new Money(r.Amount), today);
}
