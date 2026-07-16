using Carter;
using Erp.Shared.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

// The wire shape — the JSON body this route accepts. Kept separate from CreateSalesOrderCommand so the HTTP
// contract and the mediator contract can evolve independently (the convention every endpoint follows).
public sealed record CreateSalesOrderRequest(long CustomerId, IReadOnlyList<CreateSalesOrderLineDto> Lines);

public sealed class CreateSalesOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/sales-orders", async (CreateSalesOrderRequest body, ISender sender, CancellationToken ct) =>
        {
            var id = await sender.Send(new CreateSalesOrderCommand(body.CustomerId, body.Lines), ct);
            return Results.Created($"/sales-orders/{id}", new { id });
        })
        .RequireAuthorization(p => p.RequireRole("SalesRep", "SalesManager", "Admin"));
}
