using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed record UpdateSalesOrderRequest(IReadOnlyList<OrderLineInput> Lines);

internal sealed class UpdateSalesOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPut("/sales-orders/{id:long}",
            async (long id, UpdateSalesOrderRequest body, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new UpdateSalesOrderCommand(id, body.Lines), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Sellers));
}
