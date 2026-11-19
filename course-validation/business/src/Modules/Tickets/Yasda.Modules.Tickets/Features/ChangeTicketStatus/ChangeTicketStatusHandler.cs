using Yasda.Modules.Tickets.Domain;
using Yasda.Modules.Tickets.Infrastructure;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Features;

// The steps in a ticket's life after it's filed. Which step is allowed from which status is the ticket's call.
internal enum StatusChange { WaitOnRequester, Resolve, Close, Reopen, Cancel }

internal sealed record ChangeTicketStatusCommand(long TicketId, StatusChange Change, string? Reason = null);

internal sealed class ChangeTicketStatusHandler(TicketsDbContext db, ICurrentUser currentUser, TimeProvider time)
{
    public async Task<bool> HandleAsync(ChangeTicketStatusCommand cmd, CancellationToken ct)
    {
        var isStaff = TicketRoles.IsHandler(currentUser.Role);
        var ticket = await db.GetVisibleTicketAsync(cmd.TicketId, new UserId(currentUser.UserId), isStaff, ct);
        if (ticket is null) return false;

        var now = time.GetUtcNow().UtcDateTime;
        switch (cmd.Change)
        {
            case StatusChange.WaitOnRequester:
                ticket.MarkPending(now);
                break;
            case StatusChange.Resolve:
                ticket.Resolve(now);
                break;
            case StatusChange.Close:
                ticket.Close(now);
                break;
            case StatusChange.Reopen:
                ticket.Reopen(now);
                break;
            case StatusChange.Cancel:
                ticket.Cancel(cmd.Reason ?? "", now);
                break;
        }

        await db.SaveChangesAsync(ct);
        return true;
    }
}
