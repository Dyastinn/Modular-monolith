using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Features;

// ApprovalThreshold: null = the company's approval limit applies.
internal sealed record CustomerInput(string Name, string? Tin, string? Address, decimal CreditLimit, int PaymentTermsDays,
    decimal? ApprovalThreshold = null);

internal sealed record CreateCustomerCommand(CustomerInput Customer);

internal sealed class CreateCustomerHandler(SalesDbContext db)
{
    public async Task<long> HandleAsync(CreateCustomerCommand cmd, CancellationToken ct)
    {
        var c = cmd.Customer;
        var customer = Customer.Create(c.Name, c.Tin, c.Address, new Money(c.CreditLimit), c.PaymentTermsDays, c.ApprovalThresholdMoney());
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
        return customer.Id.Value;
    }
}

internal static class CustomerInputExtensions
{
    public static Money? ApprovalThresholdMoney(this CustomerInput c) => c.ApprovalThreshold is { } t ? new Money(t) : null;
}
