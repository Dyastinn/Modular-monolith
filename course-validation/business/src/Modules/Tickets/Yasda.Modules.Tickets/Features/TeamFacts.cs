using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Tickets.Domain;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Features;

// What the handlers ask Identity about teams, so the domain can decide. Tickets never reads a team table.
internal static class TeamFacts
{
    // The team Identity knows by this number, or null when none was given. A number that is no team is refused with its code.
    public static async Task<TeamRef?> FindAsync(ITeamDirectory teams, long? teamId, CancellationToken ct)
    {
        if (teamId is null) return null;

        var info = await teams.FindTeamAsync(teamId.Value, ct);
        if (info is null) throw new DomainException(TicketsErrors.UnknownTeam);
        return new TeamRef(new TeamId(info.Id), info.Name, info.IsActive);
    }

    // Is this person in the ticket's team? Only asked when the ticket has one: a ticket with no team needs no membership.
    public static async Task<bool> IsMemberAsync(ITeamDirectory teams, Ticket ticket, long userId, CancellationToken ct)
    {
        if (ticket.TeamId is null) return false;
        return await teams.IsMemberAsync(ticket.TeamId.Value.Value, userId, ct);
    }
}
