using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Tickets.Features;

internal static class TakeTicketEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/tickets/{id:long}/take", async Task<Results<NoContent, NotFound>> (long id, TakeTicketHandler handler, CancellationToken ct) =>
                await handler.HandleAsync(new TakeTicketCommand(id), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(TicketRoles.Handlers));
}
