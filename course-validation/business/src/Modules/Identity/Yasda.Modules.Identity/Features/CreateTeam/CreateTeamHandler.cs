using Yasda.Modules.Identity.Domain;
using Yasda.Modules.Identity.Infrastructure;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Identity.Features;

internal sealed record CreateTeamCommand(string Name);

internal sealed class CreateTeamHandler(YasdaIdentityDbContext db, TimeProvider time)
{
    public async Task<long> HandleAsync(CreateTeamCommand cmd, CancellationToken ct)
    {
        // Whether the name is taken is a fact the database knows; whether that is allowed is the team's call.
        var pattern = SearchText.ForLike(cmd.Name);
        var nameTaken = pattern is not null && await db.Teams.AnyAsync(t => EF.Functions.ILike(t.Name, pattern), ct);
        var team = Team.Create(cmd.Name, nameTaken, time.GetUtcNow().UtcDateTime);

        db.Teams.Add(team);
        await db.SaveChangesAsync(ct);
        return team.Id.Value;
    }
}
