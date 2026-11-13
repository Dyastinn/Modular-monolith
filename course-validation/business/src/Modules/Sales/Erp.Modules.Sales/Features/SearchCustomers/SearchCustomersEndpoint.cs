using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Sales.Features;

internal static class SearchCustomersEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/customers", async ([AsParameters] SearchCustomersQuery query, SearchCustomersHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(query, ct)))
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Readers));

        app.MapGet("/customers/{id:long}",
                async Task<Results<Ok<CustomerDto>, NotFound>> (long id, SearchCustomersHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new GetCustomerQuery(id), ct) is { } customer ? TypedResults.Ok(customer) : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(SalesRoles.Readers));
    }
}