using Yasda.Modules.Tickets.Domain;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Tickets.Features;

internal sealed record CancelTicketRequest(string Reason);

internal sealed class CancelTicketRequestValidator : AbstractValidator<CancelTicketRequest>
{
    public CancelTicketRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(Ticket.ReasonMaxLength);
    }
}

internal static class ChangeTicketStatusEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // Only staff move a ticket forward.
        app.MapPost("/tickets/{id:long}/pending", async Task<Results<NoContent, NotFound>> (long id, ChangeTicketStatusHandler handler, CancellationToken ct) =>
        {
            var found = await handler.HandleAsync(new ChangeTicketStatusCommand(id, StatusChange.WaitOnRequester), ct);
            return Answer(found);
        })
        .RequireAuthorization(policy => policy.RequireRole(TicketRoles.Handlers));

        app.MapPost("/tickets/{id:long}/resolve", async Task<Results<NoContent, NotFound>> (long id, ChangeTicketStatusHandler handler, CancellationToken ct) =>
        {
            var found = await handler.HandleAsync(new ChangeTicketStatusCommand(id, StatusChange.Resolve), ct);
            return Answer(found);
        })
        .RequireAuthorization(policy => policy.RequireRole(TicketRoles.Handlers));

        app.MapPost("/tickets/{id:long}/close", async Task<Results<NoContent, NotFound>> (long id, ChangeTicketStatusHandler handler, CancellationToken ct) =>
        {
            var found = await handler.HandleAsync(new ChangeTicketStatusCommand(id, StatusChange.Close), ct);
            return Answer(found);
        })
        .RequireAuthorization(policy => policy.RequireRole(TicketRoles.Handlers));

        // The requester may also reopen or cancel their own ticket: the handler only finds tickets they may see.
        app.MapPost("/tickets/{id:long}/reopen", async Task<Results<NoContent, NotFound>> (long id, ChangeTicketStatusHandler handler, CancellationToken ct) =>
        {
            var found = await handler.HandleAsync(new ChangeTicketStatusCommand(id, StatusChange.Reopen), ct);
            return Answer(found);
        })
        .RequireAuthorization();

        app.MapPost("/tickets/{id:long}/cancel", async Task<Results<NoContent, NotFound>> (long id, CancelTicketRequest body, ChangeTicketStatusHandler handler, CancellationToken ct) =>
        {
            var found = await handler.HandleAsync(new ChangeTicketStatusCommand(id, StatusChange.Cancel, body.Reason), ct);
            return Answer(found);
        })
        .RequireAuthorization();
    }

    private static Results<NoContent, NotFound> Answer(bool found)
    {
        if (!found) return TypedResults.NotFound();
        return TypedResults.NoContent();
    }
}
