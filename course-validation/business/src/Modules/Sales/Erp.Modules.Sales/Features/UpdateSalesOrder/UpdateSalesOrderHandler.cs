using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Sales.Features;

internal sealed record UpdateSalesOrderCommand(long OrderId, IReadOnlyList<CreateSalesOrderLineDto> Lines) : ICommand;

internal sealed class UpdateSalesOrderValidator : AbstractValidator<UpdateSalesOrderCommand>
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

internal sealed class UpdateSalesOrderHandler(SalesDbContext db, ICurrentUser currentUser)
    : ICommandHandler<UpdateSalesOrderCommand>
{
    public async ValueTask<Unit> Handle(UpdateSalesOrderCommand cmd, CancellationToken ct)
    {
        var orderId = new SalesOrderId(cmd.OrderId);
        var order = await db.SalesOrders.SingleOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new NotFoundException($"Sales order {cmd.OrderId} not found.");

        order.ClearLines();

        var customer = await db.Customers.SingleAsync(c => c.Id == order.CustomerId, ct);
        customer.EnsureNotBlocked();

        var itemIds = cmd.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.Include(i => i.Category)
            .Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);

        var qtyByCategory = new Dictionary<long, int>();
        foreach (var line in cmd.Lines)
        {
            if (!items.TryGetValue(line.ItemId, out var item) || !item.IsActive)
                throw new NotFoundException($"Item {line.ItemId} not found or inactive.");

            item.Category.EnsureRoleAllowed(currentUser.Role);
            qtyByCategory[item.CategoryId] = qtyByCategory.GetValueOrDefault(item.CategoryId) + line.Quantity;
            item.Category.EnsureQuantityAllowed(qtyByCategory[item.CategoryId]);

            order.AddLine(new ItemId(item.Id), new Quantity(line.Quantity), item.UnitPrice);
        }

        customer.EnsureWithinCreditLimit(order.Total, await db.PendingTotalAsync(customer.Id, order.Id, ct));

        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
