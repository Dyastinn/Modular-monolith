using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;


internal static class UpdateCustomerEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPut("/customers/{id:long}",
                async Task<Results<NoContent, NotFound>> (long id, CustomerInput body, UpdateCustomerHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new UpdateCustomerCommand(id, body), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Managers));

        app.MapPost("/customers/{id:long}/block",
                async Task<Results<NoContent, NotFound>> (long id, SetCustomerBlockedHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new SetCustomerBlockedCommand(id, true), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Managers));

        app.MapPost("/customers/{id:long}/unblock",
                async Task<Results<NoContent, NotFound>> (long id, SetCustomerBlockedHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new SetCustomerBlockedCommand(id, false), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Managers));
    }
}