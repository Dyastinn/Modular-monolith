using Yasda.Modules.Tickets.Domain;
using Yasda.Modules.Tickets.Infrastructure;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Features;

internal sealed record ChangePriorityCommand(long TicketId, TicketPriority Priority);

internal sealed class ChangePriorityHandler(TicketsDbContext db, ICurrentUser currentUser, TimeProvider time)
{
    public async Task<bool> HandleAsync(ChangePriorityCommand cmd, CancellationToken ct)
    {
        var ticket = await db.GetVisibleTicketAsync(cmd.TicketId, new UserId(currentUser.UserId), userIsStaff: true, ct);
        if (ticket is null) return false;

        ticket.ChangePriority(cmd.Priority, time.GetUtcNow().UtcDateTime);

        await db.SaveChangesAsync(ct);
        return true;
    }
}
