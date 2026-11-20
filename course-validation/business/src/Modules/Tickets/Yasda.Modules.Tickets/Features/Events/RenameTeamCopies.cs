using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Tickets.Domain;
using Yasda.Modules.Tickets.Infrastructure;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Tickets.Features;

// A team was renamed: bring the copies of its name that categories and tickets carry up to date. The event carries a name,
// but two quick renames can be delivered in either order, so the current name is asked from Identity instead. Setting a name
// that is already set changes nothing, so a repeated delivery is harmless. (The ticket rows are updated in one statement, which
// also moves their row version: an agent saving one of them at that very moment gets the usual "someone changed this" answer.)
internal sealed class RenameTeamCopies(TicketsDbContext db, ITeamDirectory teams) : IIntegrationEventHandler<TeamRenamed>
{
    public async Task HandleAsync(TeamRenamed integrationEvent, CancellationToken ct)
    {
        var team = await teams.FindTeamAsync(integrationEvent.TeamId, ct);
        if (team is null) return;

        var id = new TeamId(integrationEvent.TeamId);
        var name = team.Name;
        await db.Categories.Where(c => c.TeamId == id && c.TeamName != name)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.TeamName, name), ct);
        await db.Tickets.Where(t => t.TeamId == id && t.TeamName != name)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.TeamName, name), ct);
    }
}
