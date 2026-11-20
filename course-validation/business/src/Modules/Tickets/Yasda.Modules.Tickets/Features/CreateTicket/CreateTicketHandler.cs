using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Tickets.Domain;
using Yasda.Modules.Tickets.Infrastructure;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Tickets.Features;

internal sealed record CreateTicketCommand(string Subject, string Message, TicketPriority Priority, long? CategoryId);

internal sealed class CreateTicketHandler(TicketsDbContext db, ICurrentUser currentUser, IUserDirectory users, TimeProvider time)
{
    public async Task<long> HandleAsync(CreateTicketCommand cmd, CancellationToken ct)
    {
        // The name is copied onto the ticket, so the queues can show it without reading Identity's tables.
        var name = await users.NameOfAsync(currentUser.UserId, ct) ?? currentUser.Username;
        // The category is optional ("Not sure" leaves the ticket unrouted). Whether the one chosen may be used is the ticket's call.
        Category? category = null;
        if (cmd.CategoryId is not null)
        {
            var categoryId = new CategoryId(cmd.CategoryId.Value);
            category = await db.Categories.SingleOrDefaultAsync(c => c.Id == categoryId, ct);
        }

        var id = await db.NextTicketIdAsync(ct);
        var ticket = Ticket.Create(id, cmd.Subject, cmd.Message, cmd.Priority, new UserId(currentUser.UserId), name,
            time.GetUtcNow().UtcDateTime, category, categoryChosen: cmd.CategoryId is not null);

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync(ct);
        return ticket.Id.Value;
    }
}
