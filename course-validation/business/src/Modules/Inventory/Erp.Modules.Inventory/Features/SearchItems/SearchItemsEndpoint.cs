using Carter;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Inventory.Features;

internal sealed class SearchItemsEndpoint : ICarterModule
{
    // Any signed-in user: buyers pick items for purchase orders, reps check what's in stock.
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet("/items", async (string? search, bool? activeOnly, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new SearchItemsQuery(search, activeOnly ?? false, page ?? 1, pageSize ?? 20), ct)))
            .RequireAuthorization();
}
