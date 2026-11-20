using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Tickets.Domain;
using Yasda.Modules.Tickets.Infrastructure;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Features;

internal sealed record AssignTicketCommand(long TicketId, long AssigneeId);

internal sealed class AssignTicketHandler(TicketsDbContext db, ICurrentUser currentUser, IUserDirectory users, ITeamDirectory teams, TimeProvider time)
{
    public async Task<bool> HandleAsync(AssignTicketCommand cmd, CancellationToken ct)
    {
        var me = new UserId(currentUser.UserId);
        var ticket = await db.GetVisibleTicketAsync(cmd.TicketId, me, userIsStaff: true, ct);
        if (ticket is null) return false;

        // Only Identity knows who is staff. An id in the body that isn't, is refused with its code.
        var assignee = await users.FindStaffAsync(cmd.AssigneeId, ct);
        if (assignee is null)
            throw new DomainException(TicketsErrors.AssigneeNotStaff, $"User {cmd.AssigneeId} isn't an active agent, team lead or admin.");

        var isMember = await TeamFacts.IsMemberAsync(teams, ticket, assignee.Id, ct);
        var isAdmin = assignee.Role == Roles.Admin;
        ticket.Assign(new UserId(assignee.Id), assignee.FullName, time.GetUtcNow().UtcDateTime, isMember, isAdmin);

        await db.SaveChangesAsync(ct);
        return true;
    }
}
