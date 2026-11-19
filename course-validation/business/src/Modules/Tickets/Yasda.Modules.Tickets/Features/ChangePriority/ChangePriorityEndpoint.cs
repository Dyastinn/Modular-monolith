using Yasda.Modules.Tickets.Domain;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Tickets.Features;

internal sealed record ChangePriorityRequest(TicketPriority Priority);

internal sealed class ChangePriorityRequestValidator : AbstractValidator<ChangePriorityRequest>
{
    public ChangePriorityRequestValidator() => RuleFor(x => x.Priority).IsInEnum();
}

internal static class ChangePriorityEndpoint
{
    // The requester suggests a priority when filing; after that, staff decide.
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPut("/tickets/{id:long}/priority",
                async Task<Results<NoContent, NotFound>> (long id, ChangePriorityRequest body, ChangePriorityHandler handler, CancellationToken ct) =>
                    await handler.HandleAsync(new ChangePriorityCommand(id, body.Priority), ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .RequireAuthorization(p => p.RequireRole(TicketRoles.Handlers));
}
