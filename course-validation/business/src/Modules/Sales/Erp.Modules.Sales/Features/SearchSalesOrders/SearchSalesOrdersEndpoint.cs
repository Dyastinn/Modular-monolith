using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed class SearchSalesOrdersEndpoint : ICarterModule
{
    // int? (not int): minimal APIs treat a non-nullable int query parameter as REQUIRED, so a bare
    // GET /sales-orders would 400 before the handler ever ran.
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet("/sales-orders",
            async (long? customerId, string? status, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(
                    new SearchSalesOrdersQuery(customerId, status, page ?? 1, pageSize ?? 20), ct)))
            .RequireAuthorization();
}
