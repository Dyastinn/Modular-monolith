using Carter;
using Erp.Shared.BuildingBlocks;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed record CreateSalesOrderRequest(long CustomerId, IReadOnlyList<CreateSalesOrderLineDto> Lines);

internal sealed class CreateSalesOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/sales-orders", async (CreateSalesOrderRequest body, ISender sender, CancellationToken ct) =>
        {
            var id = await sender.Send(new CreateSalesOrderCommand(body.CustomerId, body.Lines), ct);
            return Results.Created($"/sales-orders/{id}", new { id });
        })
        .RequireAuthorization(p => p.RequireRole(Roles.SalesRep, Roles.SalesManager, Roles.Admin));
}
