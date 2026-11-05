using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;

namespace Erp.Modules.Sales.Features;

internal sealed record UpdateCustomerCommand(long CustomerId, CustomerInput Customer) : ICommand;

internal sealed class UpdateCustomerValidator : AbstractValidator<UpdateCustomerCommand>
{
    public UpdateCustomerValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.Customer).SetValidator(new CustomerInputValidator());
    }
}

internal sealed class UpdateCustomerHandler(SalesDbContext db) : ICommandHandler<UpdateCustomerCommand>
{
    public async ValueTask<Unit> Handle(UpdateCustomerCommand cmd, CancellationToken ct)
    {
        var customer = await db.Customers.FindAsync([new CustomerId(cmd.CustomerId)], ct)
            ?? throw new NotFoundException($"Customer {cmd.CustomerId} not found.");
        var c = cmd.Customer;
        customer.Update(c.Name, c.Tin, c.Address, new Money(c.CreditLimit), c.PaymentTermsDays);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

internal sealed record SetCustomerBlockedCommand(long CustomerId, bool Blocked) : ICommand;

internal sealed class SetCustomerBlockedHandler(SalesDbContext db) : ICommandHandler<SetCustomerBlockedCommand>
{
    public async ValueTask<Unit> Handle(SetCustomerBlockedCommand cmd, CancellationToken ct)
    {
        var customer = await db.Customers.FindAsync([new CustomerId(cmd.CustomerId)], ct)
            ?? throw new NotFoundException($"Customer {cmd.CustomerId} not found.");
        if (cmd.Blocked) customer.Block(); else customer.Unblock();
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
