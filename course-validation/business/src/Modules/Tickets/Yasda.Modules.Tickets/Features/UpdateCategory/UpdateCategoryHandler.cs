using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Tickets.Domain;
using Yasda.Modules.Tickets.Infrastructure;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;

namespace Yasda.Modules.Tickets.Features;

internal sealed record UpdateCategoryCommand(long CategoryId, string Name, long? TeamId, bool IsActive);

internal sealed class UpdateCategoryHandler(TicketsDbContext db, ITeamDirectory teams, TimeProvider time)
{
    public async Task<bool> HandleAsync(UpdateCategoryCommand cmd, CancellationToken ct)
    {
        var id = new CategoryId(cmd.CategoryId);
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (category is null) return false;

        var pattern = SearchText.ForLike(cmd.Name);
        var nameTaken = pattern is not null && await db.Categories.AnyAsync(c => c.Id != id && EF.Functions.ILike(c.Name, pattern), ct);
        var team = await TeamFacts.FindAsync(teams, cmd.TeamId, ct);

        category.Update(cmd.Name, nameTaken, team, cmd.IsActive, time.GetUtcNow().UtcDateTime);

        await db.SaveChangesAsync(ct);
        return true;
    }
}
