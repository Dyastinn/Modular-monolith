using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Tickets.Domain;
using Yasda.Modules.Tickets.Infrastructure;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Features;

internal sealed record ReplyToTicketCommand(long TicketId, string Body, bool IsInternal);

internal sealed class ReplyToTicketHandler(TicketsDbContext db, ICurrentUser currentUser, IUserDirectory users, ITeamDirectory teams, TimeProvider time)
{
    public async Task<bool> HandleAsync(ReplyToTicketCommand cmd, CancellationToken ct)
    {
        var me = new UserId(currentUser.UserId);
        var isStaff = TicketRoles.IsHandler(currentUser.Role);
        var ticket = await db.GetVisibleTicketAsync(cmd.TicketId, me, isStaff, ct);
        if (ticket is null) return false;

        var myName = await users.NameOfAsync(currentUser.UserId, ct) ?? currentUser.Username;
        // An agent's answer to an unowned ticket takes it, so the same team rule as Take applies.
        var isMember = false;
        if (isStaff) isMember = await TeamFacts.IsMemberAsync(teams, ticket, currentUser.UserId, ct);
        var isAdmin = currentUser.Role == Roles.Admin;
        ticket.Reply(me, myName, cmd.Body, cmd.IsInternal, isStaff, time.GetUtcNow().UtcDateTime, isMember, isAdmin);

        await db.SaveChangesAsync(ct);
        return true;
    }
}
