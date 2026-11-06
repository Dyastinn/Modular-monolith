using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;

namespace Erp.Modules.Sales.Features;

internal sealed record SetItemPricingCommand(long ItemId, decimal UnitPrice, long CategoryId, bool IsForSale, TaxType TaxType) : ICommand;

internal sealed class SetItemPricingValidator : AbstractValidator<SetItemPricingCommand>
{
    public SetItemPricingValidator()
    {
        RuleFor(x => x.ItemId).GreaterThan(0);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CategoryId).GreaterThan(0);
        RuleFor(x => x.TaxType).IsInEnum();
    }
}

internal sealed class SetItemPricingHandler(SalesDbContext db) : ICommandHandler<SetItemPricingCommand>
{
    public async ValueTask<Unit> Handle(SetItemPricingCommand cmd, CancellationToken ct)
    {
        var item = await db.SalesItems.FindAsync([new ItemId(cmd.ItemId)], ct)
            ?? throw new NotFoundException($"Item {cmd.ItemId} not found. New catalog items appear here within a few seconds.");
        var category = await db.ItemCategories.FindAsync([new CategoryId(cmd.CategoryId)], ct)
            ?? throw new NotFoundException($"Category {cmd.CategoryId} not found.");

        item.SetPricing(new Money(cmd.UnitPrice), category, cmd.IsForSale, cmd.TaxType);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
