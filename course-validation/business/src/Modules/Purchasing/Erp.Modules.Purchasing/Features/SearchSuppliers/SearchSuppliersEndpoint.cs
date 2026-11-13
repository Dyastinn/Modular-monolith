using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Purchasing.Features;

internal static class SearchSuppliersEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/suppliers", async ([AsParameters] SearchSuppliersQuery query, SearchSuppliersHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(query, ct)))
            .RequireAuthorization(p => p.RequireRole(PurchasingRoles.Readers));

        app.MapGet("/suppliers/{id:long}",
                async Task<Results<Ok<SupplierDto>, NotFound>> (long id, SearchSuppliersHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new GetSupplierQuery(id), ct) is { } s ? TypedResults.Ok(s) : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(PurchasingRoles.Readers));
    }
}