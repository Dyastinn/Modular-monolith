using Carter;
using Erp.Shared.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features.CreateSalesOrder;

// ── Request: the HTTP body shape ───────────────────────────────────────
public sealed record CreateSalesOrderRequest(long CustomerId, List<OrderLineInput> Lines);

// ── Response: the HTTP response shape ─────────────────────────────────
public sealed record CreateSalesOrderResponse(long SalesOrderId);

// ── Endpoint: a Carter module living right next to its handler ─────────
public sealed class CreateSalesOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/sales-orders", async (
            CreateSalesOrderRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateSalesOrderCommand(request.CustomerId, request.Lines), ct);
            return Results.Created($"/sales-orders/{result.SalesOrderId}",
                new CreateSalesOrderResponse(result.SalesOrderId));
        });
}
