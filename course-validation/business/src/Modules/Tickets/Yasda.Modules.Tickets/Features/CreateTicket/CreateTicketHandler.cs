using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Tickets.Domain;
using Yasda.Modules.Tickets.Infrastructure;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Features;

internal sealed record CreateTicketCommand(string Subject, string Message, TicketPriority Priority);

internal sealed class CreateTicketHandler(TicketsDbContext db, ICurrentUser currentUser, IUserDirectory users, TimeProvider time)
{
    public async Task<long> HandleAsync(CreateTicketCommand cmd, CancellationToken ct)
    {
        // The name is copied onto the ticket, so the queues can show it without reading Identity's tables.
        var name = await users.NameOfAsync(currentUser.UserId, ct) ?? currentUser.Username;
        var id = await db.NextTicketIdAsync(ct);
        var ticket = Ticket.Create(id, cmd.Subject, cmd.Message, cmd.Priority, new UserId(currentUser.UserId), name,
            time.GetUtcNow().UtcDateTime);

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync(ct);
        return ticket.Id.Value;
    }
}
