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
    public CancelTicketRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(Ticket.ReasonMaxLength);
}

internal static class ChangeTicketStatusEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // Only staff move a ticket forward.
        app.MapPost("/tickets/{id:long}/pending", (long id, ChangeTicketStatusHandler handler, CancellationToken ct) =>
                Change(handler, new ChangeTicketStatusCommand(id, StatusChange.WaitOnRequester), ct))
            .RequireAuthorization(p => p.RequireRole(TicketRoles.Handlers));
        app.MapPost("/tickets/{id:long}/resolve", (long id, ChangeTicketStatusHandler handler, CancellationToken ct) =>
                Change(handler, new ChangeTicketStatusCommand(id, StatusChange.Resolve), ct))
            .RequireAuthorization(p => p.RequireRole(TicketRoles.Handlers));
        app.MapPost("/tickets/{id:long}/close", (long id, ChangeTicketStatusHandler handler, CancellationToken ct) =>
                Change(handler, new ChangeTicketStatusCommand(id, StatusChange.Close), ct))
            .RequireAuthorization(p => p.RequireRole(TicketRoles.Handlers));

        // The requester may also reopen or cancel their own ticket: the handler only finds tickets they may see.
        app.MapPost("/tickets/{id:long}/reopen", (long id, ChangeTicketStatusHandler handler, CancellationToken ct) =>
                Change(handler, new ChangeTicketStatusCommand(id, StatusChange.Reopen), ct))
            .RequireAuthorization();
        app.MapPost("/tickets/{id:long}/cancel", (long id, CancelTicketRequest body, ChangeTicketStatusHandler handler, CancellationToken ct) =>
                Change(handler, new ChangeTicketStatusCommand(id, StatusChange.Cancel, body.Reason), ct))
            .RequireAuthorization();
    }

    private static async Task<Results<NoContent, NotFound>> Change(ChangeTicketStatusHandler handler, ChangeTicketStatusCommand cmd, CancellationToken ct) =>
        await handler.HandleAsync(cmd, ct) ? TypedResults.NoContent() : TypedResults.NotFound();
}
