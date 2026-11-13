using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Inventory.Features;


internal static class SearchItemsEndpoint
{
    // Any signed-in user: buyers pick items for purchase orders, reps check what is in stock.
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/items", async ([AsParameters] SearchItemsQuery query, SearchItemsHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(query, ct)))
            .RequireAuthorization();
}