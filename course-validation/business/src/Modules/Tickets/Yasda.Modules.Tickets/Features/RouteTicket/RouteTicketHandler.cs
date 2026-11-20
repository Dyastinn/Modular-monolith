using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Tickets.Domain;
using Yasda.Modules.Tickets.Infrastructure;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Tickets.Features;

// Both are optional: no team leaves the ticket unrouted. The team is chosen explicitly, usually the category's team.
internal sealed record RouteTicketCommand(long TicketId, long? CategoryId, long? TeamId);

internal sealed class RouteTicketHandler(TicketsDbContext db, ICurrentUser currentUser, IUserDirectory users, ITeamDirectory teams, TimeProvider time)
{
    public async Task<bool> HandleAsync(RouteTicketCommand cmd, CancellationToken ct)
    {
        var me = new UserId(currentUser.UserId);
        var ticket = await db.GetVisibleTicketAsync(cmd.TicketId, me, userIsStaff: true, ct);
        if (ticket is null) return false;

        // An id in the body that names nothing is refused with its code, as for the assignee.
        Category? category = null;
        if (cmd.CategoryId is not null)
        {
            var categoryId = new CategoryId(cmd.CategoryId.Value);
            category = await db.Categories.SingleOrDefaultAsync(c => c.Id == categoryId, ct);
            if (category is null) throw new DomainException(TicketsErrors.CategoryNotAvailable);
        }
        var team = await TeamFacts.FindAsync(teams, cmd.TeamId, ct);

        // Does the current owner belong in the new team? The ticket decides whether they keep it.
        var assigneeIsMember = false;
        var assigneeIsAdmin = false;
        if (ticket.AssigneeId is not null && team is not null)
        {
            var assigneeId = ticket.AssigneeId.Value.Value;
            assigneeIsMember = await teams.IsMemberAsync(team.Value.Id.Value, assigneeId, ct);
            var assignee = await users.FindStaffAsync(assigneeId, ct);
            assigneeIsAdmin = assignee is not null && assignee.Role == Roles.Admin;
        }

        ticket.Route(category, team, time.GetUtcNow().UtcDateTime, assigneeIsMember, assigneeIsAdmin);

        await db.SaveChangesAsync(ct);
        return true;
    }
}
