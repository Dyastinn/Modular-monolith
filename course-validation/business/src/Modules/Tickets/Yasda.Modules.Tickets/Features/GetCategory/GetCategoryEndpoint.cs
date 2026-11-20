using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Tickets.Features;

internal static class GetCategoryEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/categories/{id:long}", async Task<Results<Ok<CategoryDto>, NotFound>> (long id, GetCategoryHandler handler, CancellationToken ct) =>
        {
            var category = await handler.HandleAsync(id, ct);
            if (category is null) return TypedResults.NotFound();
            return TypedResults.Ok(category);
        })
        .RequireAuthorization(policy => policy.RequireRole(TicketRoles.Leads));
    }
}
