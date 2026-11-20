using Yasda.Modules.Identity.Domain;
using Yasda.Modules.Identity.Infrastructure;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Identity.Features;

// Changes to one team, each a small handler: load the team, ask it to change itself, save. They answer false when
// there is no such team. The rules (a name nobody has, who may be notified) are the team's own.

internal sealed record RenameTeamCommand(long TeamId, string Name);

internal sealed class RenameTeamHandler(YasdaIdentityDbContext db, TimeProvider time)
{
    public async Task<bool> HandleAsync(RenameTeamCommand cmd, CancellationToken ct)
    {
        var id = new TeamId(cmd.TeamId);
        var team = await db.Teams.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (team is null) return false;

        var pattern = SearchText.ForLike(cmd.Name);
        var nameTaken = pattern is not null && await db.Teams.AnyAsync(t => t.Id != id && EF.Functions.ILike(t.Name, pattern), ct);
        team.Rename(cmd.Name, nameTaken, time.GetUtcNow().UtcDateTime);

        await db.SaveChangesAsync(ct);
        return true;
    }
}

internal sealed record SetTeamActiveCommand(long TeamId, bool IsActive);

internal sealed class SetTeamActiveHandler(YasdaIdentityDbContext db, TimeProvider time)
{
    public async Task<bool> HandleAsync(SetTeamActiveCommand cmd, CancellationToken ct)
    {
        var id = new TeamId(cmd.TeamId);
        var team = await db.Teams.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (team is null) return false;

        team.SetActive(cmd.IsActive, time.GetUtcNow().UtcDateTime);

        await db.SaveChangesAsync(ct);
        return true;
    }
}

internal sealed record SetTeamNotifyCommand(long TeamId, NotifyMode Mode, IReadOnlyList<long> SelectedUserIds);

internal sealed class SetTeamNotifyHandler(YasdaIdentityDbContext db, TimeProvider time)
{
    public async Task<bool> HandleAsync(SetTeamNotifyCommand cmd, CancellationToken ct)
    {
        var id = new TeamId(cmd.TeamId);
        var team = await db.Teams.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (team is null) return false;

        var selected = new List<UserId>();
        foreach (var userId in cmd.SelectedUserIds)
            selected.Add(new UserId(userId));
        team.SetNotify(cmd.Mode, selected, time.GetUtcNow().UtcDateTime);

        await db.SaveChangesAsync(ct);
        return true;
    }
}
