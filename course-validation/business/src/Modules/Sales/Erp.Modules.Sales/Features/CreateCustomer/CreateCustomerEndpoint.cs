using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed class CreateCustomerEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/customers", async (CustomerInput body, ISender sender, CancellationToken ct) =>
            {
                var id = await sender.Send(new CreateCustomerCommand(body), ct);
                return Results.Created($"/api/customers/{id}", new { id });
            })
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Managers));
}
