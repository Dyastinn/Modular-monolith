using Carter;
using Erp.Shared.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

public sealed class SubmitSalesOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/sales-orders/{id:long}/submit",
            async (long id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new SubmitSalesOrderCommand(id), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole("SalesRep", "SalesManager", "Admin"));
}
