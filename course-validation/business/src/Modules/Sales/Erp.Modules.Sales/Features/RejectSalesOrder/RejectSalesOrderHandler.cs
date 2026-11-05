using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore.Storage;

namespace Erp.Modules.Sales.Features;

internal sealed record RejectSalesOrderCommand(long OrderId, string Reason) : ICommand;

internal sealed class RejectSalesOrderValidator : AbstractValidator<RejectSalesOrderCommand>
{
    public RejectSalesOrderValidator()
    {
        RuleFor(x => x.OrderId).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

internal sealed class RejectSalesOrderHandler(SalesDbContext db, IStockReservation stock, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<RejectSalesOrderCommand>
{
    public async ValueTask<Unit> Handle(RejectSalesOrderCommand cmd, CancellationToken ct)
    {
        var manager = new UserId(currentUser.UserId);
        var order = await db.GetVisibleOrderAsync(cmd.OrderId, manager, currentUser.Role, ct);

        var toRelease = order.Reject(manager, cmd.Reason, clock.UtcNow);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var line in toRelease)
            await stock.ReleaseAsync(line.ItemId.Value, line.Quantity.Value, tx.GetDbTransaction(), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Unit.Value;
    }
}
