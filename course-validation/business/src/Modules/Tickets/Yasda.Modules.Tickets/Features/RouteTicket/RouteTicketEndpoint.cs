using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Tickets.Features;

internal sealed record RouteTicketRequest(long? CategoryId, long? TeamId);

internal sealed class RouteTicketRequestValidator : AbstractValidator<RouteTicketRequest>
{
    public RouteTicketRequestValidator()
    {
        RuleFor(x => x.CategoryId).GreaterThan(0).When(x => x.CategoryId is not null);
        RuleFor(x => x.TeamId).GreaterThan(0).When(x => x.TeamId is not null);
    }
}

internal static class RouteTicketEndpoint
{
    // Sorting out where a ticket belongs is the team lead's call.
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/tickets/{id:long}/route", async Task<Results<NoContent, NotFound>> (long id, RouteTicketRequest body, RouteTicketHandler handler, CancellationToken ct) =>
        {
            var found = await handler.HandleAsync(new RouteTicketCommand(id, body.CategoryId, body.TeamId), ct);
            if (!found) return TypedResults.NotFound();
            return TypedResults.NoContent();
        })
        .RequireAuthorization(policy => policy.RequireRole(TicketRoles.Leads));
    }
}
