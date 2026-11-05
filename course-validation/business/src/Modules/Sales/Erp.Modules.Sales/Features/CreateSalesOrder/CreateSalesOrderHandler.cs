using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Sales.Features;

internal sealed record CreateSalesOrderLineDto(long ItemId, int Quantity);

internal sealed record CreateSalesOrderCommand(
    long CustomerId,
    IReadOnlyList<CreateSalesOrderLineDto> Lines) : ICommand<long>;   // returns the new order id

internal sealed class CreateSalesOrderValidator : AbstractValidator<CreateSalesOrderCommand>
{
    public CreateSalesOrderValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("An order needs at least one line.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ItemId).GreaterThan(0);
            line.RuleFor(l => l.Quantity).GreaterThan(0);
        });
    }
}

internal sealed class CreateSalesOrderHandler(SalesDbContext db, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<CreateSalesOrderCommand, long>
{
    public async ValueTask<long> Handle(CreateSalesOrderCommand cmd, CancellationToken ct)
    {
        // 1) Load the customer and check it may order (domain).
        var customerId = new CustomerId(cmd.CustomerId);
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == customerId, ct)
            ?? throw new NotFoundException($"Customer {cmd.CustomerId} not found.");
        customer.EnsureNotBlocked();

        // 2) Load every requested item with its category rules, in one query.
        var itemIds = cmd.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.Include(i => i.Category)
            .Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);

        // 3) Build the order THROUGH the domain, so every rule runs.
        var now = clock.UtcNow;
        var order = SalesOrder.Create(await db.NextOrderNumberAsync(now, ct), customer.Id, new UserId(currentUser.UserId), now);

        var qtyByCategory = new Dictionary<long, int>();
        foreach (var line in cmd.Lines)
        {
            if (!items.TryGetValue(line.ItemId, out var item) || !item.IsActive)
                throw new NotFoundException($"Item {line.ItemId} not found or inactive.");

            item.Category.EnsureRoleAllowed(currentUser.Role);

            // The category cap applies to the whole order, not each line.
            qtyByCategory[item.CategoryId] = qtyByCategory.GetValueOrDefault(item.CategoryId) + line.Quantity;
            item.Category.EnsureQuantityAllowed(qtyByCategory[item.CategoryId]);

            order.AddLine(new ItemId(item.Id), new Quantity(line.Quantity), item.UnitPrice);
        }

        customer.EnsureWithinCreditLimit(order.Total);

        // 4) One SaveChanges = one transaction: the order and all its lines, or nothing.
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync(ct);
        return order.Id.Value;   // EF filled in the database-assigned id
    }
}
