using Erp.Modules.Sales.Domain;
using Erp.Shared.Common;
using Erp.Shared.Infrastructure;
using Erp.Shared.Messaging;
using FluentValidation;

namespace Erp.Modules.Sales.Features;

public sealed record UpdateSalesOrderCommand(
    long OrderId,
    IReadOnlyList<CreateSalesOrderLineDto> Lines) : ICommand<Unit>;   // reuse the line DTO from Create

public sealed class UpdateSalesOrderValidator : AbstractValidator<UpdateSalesOrderCommand>
{
    public UpdateSalesOrderValidator()
    {
        RuleFor(x => x.OrderId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ItemId).GreaterThan(0);
            line.RuleFor(l => l.Quantity).GreaterThan(0);
        });
    }
}

public sealed class UpdateSalesOrderHandler(
    ISalesOrderRepository orders,
    ICustomerRepository customers,
    IItemRepository items,
    IUnitOfWork uow,
    ICurrentUser currentUser) : ICommandHandler<UpdateSalesOrderCommand, Unit>
{
    public async Task<Unit> Handle(UpdateSalesOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.GetAsync(new SalesOrderId(cmd.OrderId), ct)
            ?? throw new NotFoundException($"Sales order {cmd.OrderId} not found.");

        // DOMAIN guards "draft only": ClearLines throws if the order isn't a Draft.
        order.ClearLines();

        var customer = await customers.GetAsync(order.CustomerId, ct)
            ?? throw new NotFoundException("Customer not found.");
        customer.EnsureNotBlocked();

        var itemIds = cmd.Lines.Select(l => l.ItemId).Distinct().ToList();
        var itemLookup = (await items.GetForOrderAsync(itemIds, ct)).ToDictionary(i => i.ItemId);
        var qtyByCategory = new Dictionary<long, int>();

        foreach (var line in cmd.Lines)
        {
            if (!itemLookup.TryGetValue(line.ItemId, out var item) || !item.IsActive)
                throw new NotFoundException($"Item {line.ItemId} not found or inactive.");

            item.Category.EnsureRoleAllowed(currentUser.Role);
            qtyByCategory.TryGetValue(item.Category.Id, out var running);
            running += line.Quantity;
            qtyByCategory[item.Category.Id] = running;
            item.Category.EnsureQuantityAllowed(running);

            order.AddLine(new ItemId(line.ItemId), new Quantity(line.Quantity), item.UnitPrice);
        }

        customer.EnsureWithinCreditLimit(order.Total);

        await using var tx = await uow.BeginAsync(ct);
        await orders.ReplaceLinesAsync(order, tx.Transaction, ct);   // delete old lines, insert new, update total
        await tx.CommitAsync(ct);

        return Unit.Value;
    }
}
