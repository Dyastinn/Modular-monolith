using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed class GetSalesOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet("/sales-orders/{id:long}",
            async (long id, ISender sender, CancellationToken ct) =>
            {
                var dto = await sender.Send(new GetSalesOrderQuery(id), ct);
                return dto is null ? Results.NotFound() : Results.Ok(dto);
            })
            .RequireAuthorization();   // any signed-in user
}
