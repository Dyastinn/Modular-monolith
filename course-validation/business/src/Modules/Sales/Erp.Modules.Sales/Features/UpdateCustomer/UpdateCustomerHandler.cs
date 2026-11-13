using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;

namespace Erp.Modules.Sales.Features;

internal sealed record UpdateCustomerCommand(long CustomerId, CustomerInput Customer);

internal sealed class UpdateCustomerHandler(SalesDbContext db)
{
    public async Task<bool> HandleAsync(UpdateCustomerCommand cmd, CancellationToken ct)
    {
        var customer = await db.Customers.FindAsync([new CustomerId(cmd.CustomerId)], ct);
        if (customer is null) return false;
        var c = cmd.Customer;
        customer.Update(c.Name, c.Tin, c.Address, new Money(c.CreditLimit), c.PaymentTermsDays);
        await db.SaveChangesAsync(ct);
        return true;
    }
}

internal sealed record SetCustomerBlockedCommand(long CustomerId, bool Blocked);

internal sealed class SetCustomerBlockedHandler(SalesDbContext db)
{
    public async Task<bool> HandleAsync(SetCustomerBlockedCommand cmd, CancellationToken ct)
    {
        var customer = await db.Customers.FindAsync([new CustomerId(cmd.CustomerId)], ct);
        if (customer is null) return false;
        if (cmd.Blocked) customer.Block(); else customer.Unblock();
        await db.SaveChangesAsync(ct);
        return true;
    }
}
