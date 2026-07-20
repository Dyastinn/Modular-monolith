using Carter;
using Erp.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed class SearchSalesOrdersEndpoint : ICarterModule
{
    // int? (not int): a non-nullable int query parameter is REQUIRED by minimal-API model binding, so
    // `GET /sales-orders` with no query string would fail with a 400 before the handler ever ran.
    // Taught in plan-business-tutorial/10-queries-search.md §6.
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet("/sales-orders",
            async (long? customerId, string? status, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(
                    new SearchSalesOrdersQuery(customerId, status, page ?? 1, pageSize ?? 20), ct);
                return Results.Ok(result);
            })
            .RequireAuthorization();
}
