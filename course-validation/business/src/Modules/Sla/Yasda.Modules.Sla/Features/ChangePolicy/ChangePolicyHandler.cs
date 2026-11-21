using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Sla.Domain;
using Yasda.Modules.Sla.Infrastructure;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Sla.Features;

internal sealed record ChangePolicyCommand(Priority Priority, int RespondMinutes, int ResolveMinutes);

internal sealed class ChangePolicyHandler(SlaDbContext db, ICurrentUser currentUser, IUserDirectory users, TimeProvider time,
    ClockRecalculator recalculator)
{
    // false when there is no policy for that priority. Whether the numbers are allowed is the policy's call.
    public async Task<bool> HandleAsync(ChangePolicyCommand cmd, CancellationToken ct)
    {
        var policy = await db.Policies.SingleOrDefaultAsync(p => p.Priority == cmd.Priority, ct);
        if (policy is null) return false;

        // The name is copied onto the policy, so the screen can say who changed it without asking Identity again.
        var myName = await users.NameOfAsync(currentUser.UserId, ct) ?? currentUser.Username;
        policy.Change(TimeSpan.FromMinutes(cmd.RespondMinutes), TimeSpan.FromMinutes(cmd.ResolveMinutes),
            new UserId(currentUser.UserId), myName, time.GetUtcNow().UtcDateTime);

        // New numbers move the running clocks' deadlines (decision F10), so they are saved in the same step.
        var calendar = await db.GetBusinessCalendarAsync(ct);
        await recalculator.RecalculateAsync(calendar, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
