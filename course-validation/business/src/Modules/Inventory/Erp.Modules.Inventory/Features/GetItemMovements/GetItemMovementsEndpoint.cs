using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Inventory.Features;


internal static class GetItemMovementsEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/items/{itemId:long}/movements",
                async ([AsParameters] GetItemMovementsQuery query, GetItemMovementsHandler handler, CancellationToken ct) =>
                    TypedResults.Ok(await handler.HandleAsync(query, ct)))
            .RequireAuthorization();
}