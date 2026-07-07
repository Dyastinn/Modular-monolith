using Carter;
using Erp.Shared.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

public sealed class SearchSalesOrdersEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet("/sales-orders",
            async (long? customerId, string? status, int page, int pageSize, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(
                    new SearchSalesOrdersQuery(customerId, status, page == 0 ? 1 : page, pageSize == 0 ? 20 : pageSize), ct);
                return Results.Ok(result);
            })
            .RequireAuthorization();
}
