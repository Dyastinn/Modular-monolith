using Erp.Shared.BuildingBlocks;
using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed record SetItemPricingRequest(decimal UnitPrice, long CategoryId, bool IsForSale, TaxType TaxType = TaxType.Vatable);

internal sealed class SetItemPricingEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPut("/price-list/{itemId:long}", async (long itemId, SetItemPricingRequest body, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new SetItemPricingCommand(itemId, body.UnitPrice, body.CategoryId, body.IsForSale, body.TaxType), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Managers));
}
