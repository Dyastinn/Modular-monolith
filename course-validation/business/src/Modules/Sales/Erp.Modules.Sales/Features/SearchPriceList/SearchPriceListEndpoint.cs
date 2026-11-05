using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed class SearchPriceListEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet("/price-list", async (string? search, bool? forSaleOnly, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new SearchPriceListQuery(search, forSaleOnly ?? false, page ?? 1, pageSize ?? 20), ct)))
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Readers));
}
