using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Sales.Features;

internal sealed record UpdateSalesOrderCommand(long OrderId, IReadOnlyList<OrderLineInput> Lines) : ICommand;

internal sealed class UpdateSalesOrderValidator : AbstractValidator<UpdateSalesOrderCommand>
{
    public UpdateSalesOrderValidator()
    {
        RuleFor(x => x.OrderId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("An order needs at least one line.");
        RuleForEach(x => x.Lines).SetValidator(new OrderLineInputValidator());
    }
}

internal sealed class UpdateSalesOrderHandler(SalesDbContext db, ICurrentUser currentUser)
    : ICommandHandler<UpdateSalesOrderCommand>
{
    public async ValueTask<Unit> Handle(UpdateSalesOrderCommand cmd, CancellationToken ct)
    {
        var order = await db.GetVisibleOrderAsync(cmd.OrderId, new UserId(currentUser.UserId), currentUser.Role, ct);
        var customer = await db.Customers.SingleAsync(c => c.Id == order.CustomerId, ct);
        customer.EnsureNotBlocked();

        order.SetLines(await db.ResolveLinesAsync(cmd.Lines.Select(l => (l.ItemId, l.Quantity)), ct), currentUser.Role);
        customer.EnsureWithinCreditLimit(order.Total, await db.PendingTotalAsync(customer.Id, order.Id, ct));

        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
