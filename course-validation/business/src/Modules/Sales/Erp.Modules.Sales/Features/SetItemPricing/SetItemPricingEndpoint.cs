using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed record SetItemPricingRequest(decimal UnitPrice, long CategoryId, bool IsForSale, TaxType TaxType = TaxType.Vatable,
    bool SpecialDiscountEligible = false);

internal sealed class SetItemPricingRequestValidator : AbstractValidator<SetItemPricingRequest>
{
    public SetItemPricingRequestValidator()
    {
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CategoryId).GreaterThan(0);
        RuleFor(x => x.TaxType).IsInEnum();
    }
}

internal static class SetItemPricingEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPut("/price-list/{itemId:long}",
                async Task<Results<NoContent, NotFound>> (long itemId, SetItemPricingRequest body, SetItemPricingHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new SetItemPricingCommand(itemId, body.UnitPrice, body.CategoryId, body.IsForSale, body.TaxType,
                        body.SpecialDiscountEligible), ct)
                        ? TypedResults.NoContent()
                        : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Managers));
}