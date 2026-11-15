using Erp.Modules.Sales.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Sales.Infrastructure;

internal sealed class CustomerDirectoryService(SalesDbContext db) : ICustomerDirectory
{
    public async Task<IReadOnlyList<CustomerSummary>> FindByNamesAsync(IReadOnlyCollection<string> names, CancellationToken ct)
    {
        var wanted = names.Select(n => n.Trim().ToLower()).Distinct().ToList();
        return await db.Customers.AsNoTracking()
            .Where(c => wanted.Contains(c.Name.ToLower()))
            .Select(c => new CustomerSummary(c.Id.Value, c.Name, c.Tin, c.Address, c.PaymentTermsDays))
            .ToListAsync(ct);
    }
}
