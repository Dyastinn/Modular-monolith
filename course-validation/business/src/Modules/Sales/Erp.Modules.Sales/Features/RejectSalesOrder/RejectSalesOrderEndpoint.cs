using Carter;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed record RejectSalesOrderRequest(string Reason);

internal sealed class RejectSalesOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/sales-orders/{id:long}/reject",
            async (long id, RejectSalesOrderRequest body, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new RejectSalesOrderCommand(id, body.Reason), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(Roles.SalesManager, Roles.Admin));
}
