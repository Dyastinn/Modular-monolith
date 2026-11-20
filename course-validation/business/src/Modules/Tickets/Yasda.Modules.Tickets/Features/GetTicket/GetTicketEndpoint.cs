using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Tickets.Features;

internal static class GetTicketEndpoint
{
    // Someone else's ticket is a 404, not a 403: a requester can't even learn that it exists.
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/tickets/{id:long}", async Task<Results<Ok<TicketDetailDto>, NotFound>> (long id, GetTicketHandler handler, CancellationToken ct) =>
        {
            var ticket = await handler.HandleAsync(new GetTicketQuery(id), ct);
            if (ticket is null) return TypedResults.NotFound();
            return TypedResults.Ok(ticket);
        })
        .RequireAuthorization();
    }
}
