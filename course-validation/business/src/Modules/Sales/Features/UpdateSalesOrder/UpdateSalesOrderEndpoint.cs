using Carter;
using Erp.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

public sealed class UpdateSalesOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPut("/sales-orders/{id:long}",
            async (long id, UpdateSalesOrderRequest body, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new UpdateSalesOrderCommand(id, body.Lines), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole("SalesRep", "SalesManager", "Admin"));
}

// id comes from the route; lines from the body — avoids the "id in URL vs id in body" ambiguity.
public sealed record UpdateSalesOrderRequest(IReadOnlyList<CreateSalesOrderLineDto> Lines);
