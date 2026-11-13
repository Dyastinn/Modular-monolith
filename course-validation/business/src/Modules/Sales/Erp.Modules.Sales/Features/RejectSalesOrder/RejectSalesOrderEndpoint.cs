using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;


internal sealed record RejectSalesOrderRequest(string Reason);

internal sealed class RejectSalesOrderRequestValidator : AbstractValidator<RejectSalesOrderRequest>
{
    public RejectSalesOrderRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

internal static class RejectSalesOrderEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/sales-orders/{id:long}/reject",
                async Task<Results<NoContent, NotFound>> (long id, RejectSalesOrderRequest body, RejectSalesOrderHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new RejectSalesOrderCommand(id, body.Reason), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Managers));
}