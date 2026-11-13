using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Features;

internal sealed record CustomerInput(string Name, string? Tin, string? Address, decimal CreditLimit, int PaymentTermsDays);

internal sealed record CreateCustomerCommand(CustomerInput Customer);

internal sealed class CreateCustomerHandler(SalesDbContext db)
{
    public async Task<long> HandleAsync(CreateCustomerCommand cmd, CancellationToken ct)
    {
        var c = cmd.Customer;
        var customer = Customer.Create(c.Name, c.Tin, c.Address, new Money(c.CreditLimit), c.PaymentTermsDays);
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
        return customer.Id.Value;
    }
}
