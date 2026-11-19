using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Tickets.Features;

internal sealed record AssignTicketRequest(long AssigneeId);

internal sealed class AssignTicketRequestValidator : AbstractValidator<AssignTicketRequest>
{
    public AssignTicketRequestValidator() => RuleFor(x => x.AssigneeId).GreaterThan(0);
}

internal static class AssignTicketEndpoint
{
    // Handing work to someone else is the team lead's call.
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/tickets/{id:long}/assign",
                async Task<Results<NoContent, NotFound>> (long id, AssignTicketRequest body, AssignTicketHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new AssignTicketCommand(id, body.AssigneeId), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(TicketRoles.Leads));
}
