using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Identity.Domain;
using Yasda.Modules.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Identity.Features;

internal sealed record AddTeamMemberCommand(long TeamId, long UserId);

internal sealed class AddTeamMemberHandler(YasdaIdentityDbContext db, IUserDirectory users, TimeProvider time)
{
    public async Task<bool> HandleAsync(AddTeamMemberCommand cmd, CancellationToken ct)
    {
        var id = new TeamId(cmd.TeamId);
        var team = await db.Teams.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (team is null) return false;

        // Only Identity knows who is an active member of staff; whether that is enough is the team's call.
        var staff = await users.FindStaffAsync(cmd.UserId, ct);
        team.AddMember(new UserId(cmd.UserId), staff is not null, time.GetUtcNow().UtcDateTime);

        await db.SaveChangesAsync(ct);
        return true;
    }
}

internal sealed record RemoveTeamMemberCommand(long TeamId, long UserId);

internal sealed class RemoveTeamMemberHandler(YasdaIdentityDbContext db, TimeProvider time)
{
    public async Task<bool> HandleAsync(RemoveTeamMemberCommand cmd, CancellationToken ct)
    {
        var id = new TeamId(cmd.TeamId);
        var team = await db.Teams.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (team is null) return false;

        team.RemoveMember(new UserId(cmd.UserId), time.GetUtcNow().UtcDateTime);

        await db.SaveChangesAsync(ct);
        return true;
    }
}
