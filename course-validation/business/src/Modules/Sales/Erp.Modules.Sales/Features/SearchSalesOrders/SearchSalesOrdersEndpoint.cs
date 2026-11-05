using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed class SearchSalesOrdersEndpoint : ICarterModule
{
    // int?, not int: a non-nullable query parameter is required, so a bare GET would be a 400.
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet("/sales-orders",
            async (long? customerId, string? status, string? search, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(
                    new SearchSalesOrdersQuery(customerId, status, search, page ?? 1, pageSize ?? 20), ct)))
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Readers));
}
