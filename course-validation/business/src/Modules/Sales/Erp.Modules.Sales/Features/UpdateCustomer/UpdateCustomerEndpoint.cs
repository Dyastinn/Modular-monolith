using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed class UpdateCustomerEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut("/customers/{id:long}", async (long id, CustomerInput body, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new UpdateCustomerCommand(id, body), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Managers));

        app.MapPost("/customers/{id:long}/block", async (long id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new SetCustomerBlockedCommand(id, true), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Managers));

        app.MapPost("/customers/{id:long}/unblock", async (long id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new SetCustomerBlockedCommand(id, false), ct);
                return Results.NoContent();
            })
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Managers));
    }
}
