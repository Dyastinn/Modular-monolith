using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Tickets.Domain;
using Yasda.Modules.Tickets.Infrastructure;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Features;

internal sealed record TakeTicketCommand(long TicketId);

internal sealed class TakeTicketHandler(TicketsDbContext db, ICurrentUser currentUser, IUserDirectory users, ITeamDirectory teams, TimeProvider time)
{
    // Two agents clicking "Take" at the same moment both load an unassigned ticket. The first save wins; the
    // second fails on the ticket's row version (xmin) and gets a 409, so a ticket never has two owners.
    public async Task<bool> HandleAsync(TakeTicketCommand cmd, CancellationToken ct)
    {
        var me = new UserId(currentUser.UserId);
        var ticket = await db.GetVisibleTicketAsync(cmd.TicketId, me, userIsStaff: true, ct);
        if (ticket is null) return false;

        var myName = await users.NameOfAsync(currentUser.UserId, ct) ?? currentUser.Username;
        // On a ticket with a team, only its members (and admins) may take it: the ticket decides, we tell it who this is.
        var isMember = await TeamFacts.IsMemberAsync(teams, ticket, currentUser.UserId, ct);
        var isAdmin = currentUser.Role == Roles.Admin;
        ticket.Take(me, myName, time.GetUtcNow().UtcDateTime, isMember, isAdmin);

        await db.SaveChangesAsync(ct);
        return true;
    }
}
