using Erp.Modules.Sales.Domain;
using Erp.Shared.BuildingBlocks;
using FluentValidation;

namespace Erp.Modules.Sales.Features;

public sealed record CreateSalesOrderLineDto(long ItemId, int Quantity);

public sealed record CreateSalesOrderCommand(
    long CustomerId,
    IReadOnlyList<CreateSalesOrderLineDto> Lines) : ICommand<long>;   // returns the new order id

public sealed class CreateSalesOrderValidator : AbstractValidator<CreateSalesOrderCommand>
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

public sealed class CreateSalesOrderHandler(
    ICustomerRepository customers,
    IItemRepository items,
    ISalesOrderRepository orders,
    IDbConnectionFactory factory,
    ICurrentUser currentUser) : ICommandHandler<CreateSalesOrderCommand, long>
{
    public async Task<long> Handle(CreateSalesOrderCommand cmd, CancellationToken ct)
    {
        // 1) Load the customer; must exist.
        var customer = await customers.GetAsync(new CustomerId(cmd.CustomerId), ct)
            ?? throw new NotFoundException($"Customer {cmd.CustomerId} not found.");

        // 2) BUSINESS RULE (domain): customer must not be blocked.
        customer.EnsureNotBlocked();

        // 3) Load all requested items + their category rules in one go.
        var itemIds = cmd.Lines.Select(l => l.ItemId).Distinct().ToList();
        var itemLookup = (await items.GetForOrderAsync(itemIds, ct)).ToDictionary(i => i.ItemId);

        // 4) Build the order THROUGH the domain.
        var orderNumber = await orders.NextOrderNumberAsync(ct);
        var order = SalesOrder.Create(orderNumber, customer.Id, new UserId(currentUser.UserId));

        var qtyByCategory = new Dictionary<long, int>();

        foreach (var line in cmd.Lines)
        {
            if (!itemLookup.TryGetValue(line.ItemId, out var item) || !item.IsActive)
                throw new NotFoundException($"Item {line.ItemId} not found or inactive.");

            // 5) BUSINESS RULE (domain): does the caller's role satisfy the category restriction?
            item.Category.EnsureRoleAllowed(currentUser.Role);

            // Accumulate category quantity, then check the cap (across the whole order, not per line).
            qtyByCategory.TryGetValue(item.Category.Id, out var running);
            running += line.Quantity;
            qtyByCategory[item.Category.Id] = running;

            // 6) BUSINESS RULE (domain): per-order category quantity limit.
            item.Category.EnsureQuantityAllowed(running);

            // 7) Add the line through the aggregate (enforces draft-only, positive qty).
            order.AddLine(new ItemId(line.ItemId), new Quantity(line.Quantity), item.UnitPrice);
        }

        // 8) BUSINESS RULE (domain): the total must fit the customer's credit limit.
        customer.EnsureWithinCreditLimit(order.Total);

        // 9) Persist in ONE transaction (header + all lines, all-or-nothing).
        using var conn = factory.Create();
        await conn.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        try
        {
            var newId = await orders.AddAsync(order, tx, ct);   // AddAsync hands back the DB id
            tx.Commit();
            return newId;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
