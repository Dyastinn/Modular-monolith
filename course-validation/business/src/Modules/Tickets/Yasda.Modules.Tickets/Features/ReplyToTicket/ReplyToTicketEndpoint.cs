using Yasda.Modules.Tickets.Domain;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Yasda.Modules.Tickets.Features;

internal sealed record ReplyToTicketRequest(string Body, bool IsInternal = false);

internal sealed class ReplyToTicketRequestValidator : AbstractValidator<ReplyToTicketRequest>
{
    public ReplyToTicketRequestValidator()
    {
        RuleFor(x => x.Body).NotEmpty().MaximumLength(Ticket.MessageMaxLength);
    }
}

internal static class ReplyToTicketEndpoint
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // Requesters reply to their own tickets and staff to any; the handler's load decides which. Whether an
        // internal note is allowed is the ticket's call.
        app.MapPost("/tickets/{id:long}/replies", async Task<Results<NoContent, NotFound>> (long id, ReplyToTicketRequest body, ReplyToTicketHandler handler, CancellationToken ct) =>
        {
            var found = await handler.HandleAsync(new ReplyToTicketCommand(id, body.Body, body.IsInternal), ct);
            if (!found) return TypedResults.NotFound();
            return TypedResults.NoContent();
        })
        .RequireAuthorization();
    }
}
