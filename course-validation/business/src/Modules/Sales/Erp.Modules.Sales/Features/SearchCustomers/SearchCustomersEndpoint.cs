using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal sealed class SearchCustomersEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/customers", async (string? search, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new SearchCustomersQuery(search, page ?? 1, pageSize ?? 20), ct)))
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Readers));

        app.MapGet("/customers/{id:long}", async (long id, ISender sender, CancellationToken ct) =>
                await sender.Send(new GetCustomerQuery(id), ct) is { } customer ? Results.Ok(customer) : Results.NotFound())
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Readers));
    }
}
