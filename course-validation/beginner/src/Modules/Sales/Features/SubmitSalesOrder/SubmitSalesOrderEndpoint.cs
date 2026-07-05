using Carter;
using Erp.Shared.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features.SubmitSalesOrder;

public sealed record SubmitSalesOrderRequest(bool ApprovalGranted = false);

public sealed class SubmitSalesOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/sales-orders/{id:long}/submit", async (
            long id, SubmitSalesOrderRequest? request, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new SubmitSalesOrderCommand(
                id, request?.ApprovalGranted ?? false), ct);
            return Results.NoContent();
        });
}
