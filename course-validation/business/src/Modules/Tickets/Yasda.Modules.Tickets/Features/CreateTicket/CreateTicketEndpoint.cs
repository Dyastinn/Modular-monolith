using Yasda.Modules.Tickets.Domain;
using Yasda.Shared.BuildingBlocks;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Tickets.Features;

internal sealed record CreateTicketRequest(string Subject, string Message, TicketPriority Priority = TicketPriority.Normal);

internal sealed class CreateTicketRequestValidator : AbstractValidator<CreateTicketRequest>
{
    public CreateTicketRequestValidator()
    {
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(Ticket.SubjectMaxLength);
        RuleFor(x => x.Message).NotEmpty().MaximumLength(Ticket.MessageMaxLength);
        RuleFor(x => x.Priority).IsInEnum();
    }
}

internal static class CreateTicketEndpoint
{
    // Anyone signed in may ask for help.
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/tickets", async Task<Created<CreatedId>> (CreateTicketRequest body, CreateTicketHandler handler, CancellationToken ct) =>
            {
                var id = await handler.HandleAsync(new CreateTicketCommand(body.Subject, body.Message, body.Priority), ct);
                return TypedResults.Created($"/api/tickets/{id}", new CreatedId(id));
            })
            .RequireAuthorization();
}
