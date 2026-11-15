using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Sales.Features;

// Commit false: check every row and save nothing (the preview). True: save them all, or none if any row is refused.
internal sealed record ImportCustomersCommand(IReadOnlyList<CustomerInput> Rows, bool Commit);

internal sealed class ImportCustomersHandler(SalesDbContext db)
{
    public async Task<ImportResult> HandleAsync(ImportCustomersCommand cmd, CancellationToken ct)
    {
        // A customer already here, or twice in the file, is refused rather than duplicated: re-running a file is safe.
        var taken = (await db.Customers.Select(c => c.Name).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var errors = new List<ImportError>();
        var customers = new List<Customer>();

        for (var i = 0; i < cmd.Rows.Count; i++)
        {
            var r = cmd.Rows[i];
            try
            {
                var customer = Customer.Create(r.Name, r.Tin, r.Address, new Money(r.CreditLimit), r.PaymentTermsDays, r.ApprovalThresholdMoney());
                if (!taken.Add(customer.Name))
                    throw new DomainException(SalesErrors.CustomerAlreadyExists, $"A customer named '{customer.Name}' already exists.");
                customers.Add(customer);
            }
            catch (DomainException e)
            {
                errors.Add(new ImportError(i + 1, e.Message, e.Error.Code));
            }
        }

        if (errors.Count > 0 || !cmd.Commit) return new ImportResult(cmd.Rows.Count, 0, errors);
        db.Customers.AddRange(customers);
        await db.SaveChangesAsync(ct);
        return new ImportResult(cmd.Rows.Count, customers.Count, []);
    }
}
