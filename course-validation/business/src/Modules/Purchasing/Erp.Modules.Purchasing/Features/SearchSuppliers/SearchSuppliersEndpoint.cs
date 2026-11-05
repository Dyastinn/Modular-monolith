using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Purchasing.Features;

internal sealed class SearchSuppliersEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/suppliers", async (string? search, bool? activeOnly, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new SearchSuppliersQuery(search, activeOnly ?? false, page ?? 1, pageSize ?? 20), ct)))
            .RequireAuthorization(p => p.RequireRole(PurchasingRoles.Readers));

        app.MapGet("/suppliers/{id:long}", async (long id, ISender sender, CancellationToken ct) =>
                await sender.Send(new GetSupplierQuery(id), ct) is { } s ? Results.Ok(s) : Results.NotFound())
            .RequireAuthorization(p => p.RequireRole(PurchasingRoles.Readers));
    }
}
