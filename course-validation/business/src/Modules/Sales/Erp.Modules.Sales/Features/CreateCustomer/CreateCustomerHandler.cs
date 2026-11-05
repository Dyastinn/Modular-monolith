using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;

namespace Erp.Modules.Sales.Features;

internal sealed record CustomerInput(string Name, string? Tin, string? Address, decimal CreditLimit, int PaymentTermsDays);

internal sealed class CustomerInputValidator : AbstractValidator<CustomerInput>
{
    public CustomerInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Tin).MaximumLength(20);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.CreditLimit).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PaymentTermsDays).InclusiveBetween(0, 365);
    }
}

internal sealed record CreateCustomerCommand(CustomerInput Customer) : ICommand<long>;

internal sealed class CreateCustomerValidator : AbstractValidator<CreateCustomerCommand>
{
    public CreateCustomerValidator() => RuleFor(x => x.Customer).SetValidator(new CustomerInputValidator());
}

internal sealed class CreateCustomerHandler(SalesDbContext db) : ICommandHandler<CreateCustomerCommand, long>
{
    public async ValueTask<long> Handle(CreateCustomerCommand cmd, CancellationToken ct)
    {
        var c = cmd.Customer;
        var customer = Customer.Create(c.Name, c.Tin, c.Address, new Money(c.CreditLimit), c.PaymentTermsDays);
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
        return customer.Id.Value;
    }
}
