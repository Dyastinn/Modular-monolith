using Erp.Modules.Sales.Domain;
using Erp.Shared.Common;
using Erp.Shared.Infrastructure.Persistence;
using Erp.Shared.Messaging;
using FluentValidation;

namespace Erp.Modules.Sales.Features.CreateSalesOrder;

// ── Command: the inputs ────────────────────────────────────────────────
public sealed record CreateSalesOrderCommand(long CustomerId, IReadOnlyList<OrderLineInput> Lines)
    : ICommand<CreateSalesOrderResult>;

public sealed record OrderLineInput(long ProductId, int Quantity);

// ── Result: what the handler hands back ────────────────────────────────
public sealed record CreateSalesOrderResult(long SalesOrderId);

// ── Validator: is the request well-formed? (NOT business rules) ─────────
public sealed class CreateSalesOrderValidator : AbstractValidator<CreateSalesOrderCommand>
{
    public CreateSalesOrderValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.Quantity).GreaterThan(0);
            l.RuleFor(x => x.ProductId).GreaterThan(0);
        });
    }
}

// ── Handler: coordinate the steps, delegate rules to the domain ────────
public sealed class CreateSalesOrderHandler(
    ICustomerRepository customers,
    ISalesOrderRepository orders,
    IPricingService pricing,
    IUnitOfWork uow)
    : ICommandHandler<CreateSalesOrderCommand, CreateSalesOrderResult>
{
    public async Task<CreateSalesOrderResult> Handle(CreateSalesOrderCommand cmd, CancellationToken ct)
    {
        var customerId = new CustomerId(cmd.CustomerId);

        var customer = await customers.GetAsync(customerId, ct)
            ?? throw new NotFoundException($"Customer {cmd.CustomerId} not found.");
        customer.EnsureCanOrder();          // domain method: throws if on credit hold / inactive

        var order = SalesOrder.Create(customerId);
        foreach (var line in cmd.Lines)
        {
            var unitPrice = await pricing.GetUnitPrice(new ProductId(line.ProductId), ct);
            order.AddLine(new ProductId(line.ProductId), new Quantity(line.Quantity), unitPrice);
        }

        await using var tx = await uow.BeginAsync(ct);   // transaction boundary — kept visible
        var newId = await orders.AddAsync(order, tx.Inner, ct);   // AddAsync returns the DB-assigned id
        await tx.CommitAsync(ct);

        return new CreateSalesOrderResult(newId.Value);
    }
}
